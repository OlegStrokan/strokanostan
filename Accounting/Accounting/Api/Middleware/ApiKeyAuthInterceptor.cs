using System.Security.Cryptography;
using System.Text;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Api.Middleware;

public sealed class ApiKeyAuthInterceptor(
    IConfiguration configuration,
    ILogger<ApiKeyAuthInterceptor> logger) : Interceptor
{
    private const string ApiKeyHeader = "x-internal-api-key";
    private const string AdminServiceSegment = "/accounting.adminops.AdminAccountingService/";

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        // Two callers, two credentials. Order holds the Accounting key for the money-posting API;
        // OpsConsole holds its own key and reaches the read/adjustment surface only. One shared key
        // would let the console call ReverseRevenue.
        var isAdminSurface = context.Method.StartsWith(AdminServiceSegment, StringComparison.Ordinal);

        var configKey = isAdminSurface
            ? "InternalServices:OpsConsoleApiKey"
            : "InternalServices:AccountingApiKey";

        var expectedKey = configuration[configKey];

        if (string.IsNullOrEmpty(expectedKey))
        {
            logger.LogError("{ConfigKey} is not configured; rejecting call to {Method}.", configKey, context.Method);
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Caller not authorized."));
        }

        var providedKey = context.RequestHeaders.GetValue(ApiKeyHeader) ?? string.Empty;

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(providedKey),
                Encoding.UTF8.GetBytes(expectedKey)))
        {
            logger.LogWarning(
                "Rejected gRPC call to {Method} with missing/invalid {Header}.", context.Method, ApiKeyHeader);
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Caller not authorized."));
        }

        return await continuation(request, context);
    }
}
