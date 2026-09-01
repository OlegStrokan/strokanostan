using Application.DTOs;
using Grpc.Core;
using Infrastructure.Gateways;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Protos.Accounting;

namespace Infrastructure.Tests.Gateways;

public class AccountingGatewayTests
{
    private readonly AccountingService.AccountingServiceClient _client =
        Substitute.For<AccountingService.AccountingServiceClient>();

    private readonly ILogger<AccountingGateway> _logger =
        Substitute.For<ILogger<AccountingGateway>>();

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InternalServices:AccountingApiKey"] = "test-shared-secret"
            })
            .Build();

    private AccountingGateway Build() => new(_client, BuildConfiguration(), _logger);

    private static AsyncUnaryCall<T> GrpcCall<T>(T response) =>
        new AsyncUnaryCall<T>(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

    private static AsyncUnaryCall<T> GrpcFail<T>(StatusCode code, string detail) =>
        new AsyncUnaryCall<T>(
            Task.FromException<T>(new RpcException(new Status(code, detail))),
            Task.FromResult(new Metadata()),
            () => new Status(code, detail),
            () => new Metadata(),
            () => { });
    
    [Fact]
    public async Task ReverseRevenueAsync_ShouldReturnReversalId_WhenSucceeds()
    {
        var reversalId = "rev-555";
        var returnRequestId = Guid.NewGuid();
        ReverseRevenueRequest? sent = null;
        _client
            .ReverseRevenueAsync(Arg.Do<ReverseRevenueRequest>(r => sent = r),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcCall(new ReverseRevenueResponse { Success = true, ReversalId = reversalId }));

        var result = await Build().ReverseRevenueAsync(
            Guid.NewGuid(), returnRequestId, 100m, "USD", new List<OrderItemDto>(), CancellationToken.None);

        Assert.Equal(reversalId, result);
        Assert.Equal(returnRequestId.ToString(), sent?.ReturnRequestId);
    }

    [Fact]
    public async Task ReverseRevenueAsync_ShouldSendInternalApiKeyHeader()
    {
        Metadata? sentHeaders = null;
        _client
            .ReverseRevenueAsync(Arg.Any<ReverseRevenueRequest>(),
                Arg.Do<Metadata>(m => sentHeaders = m), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcCall(new ReverseRevenueResponse { Success = true, ReversalId = "rev-1" }));

        await Build().ReverseRevenueAsync(
            Guid.NewGuid(), Guid.NewGuid(), 50m, "USD", [], CancellationToken.None);

        Assert.Equal("test-shared-secret", sentHeaders?.GetValue("x-internal-api-key"));
    }

    [Fact]
    public async Task ReverseRevenueAsync_ShouldThrowInvalidOperation_WhenNotSuccess()
    {
        _client
            .ReverseRevenueAsync(Arg.Any<ReverseRevenueRequest>(),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcCall(new ReverseRevenueResponse { Success = false, ErrorMessage = "reversal failed" }));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Build().ReverseRevenueAsync(Guid.NewGuid(), Guid.NewGuid(), 100m, "USD", new List<OrderItemDto>(), CancellationToken.None));
    }

    [Fact]
    public async Task ReverseRevenueAsync_ShouldThrowInvalidOperation_WhenRpcNotFound()
    {
        _client
            .ReverseRevenueAsync(Arg.Any<ReverseRevenueRequest>(),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcFail<ReverseRevenueResponse>(StatusCode.NotFound, "order not in accounting"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Build().ReverseRevenueAsync(Guid.NewGuid(), Guid.NewGuid(), 100m, "USD", new List<OrderItemDto>(), CancellationToken.None));
    }

    [Fact]
    public async Task CancelRevenueReversalAsync_ShouldComplete_WhenSucceeds()
    {
        _client
            .CancelReversalAsync(Arg.Any<CancelReversalRequest>(),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcCall(new CancelReversalResponse { Success = true }));

        await Build().CancelRevenueReversalAsync("rev-1", "mistake", CancellationToken.None);
    }

    [Fact]
    public async Task CancelRevenueReversalAsync_ShouldNotThrow_WhenRpcNotFound_IdempotentCancel()
    {
        // NotFound = already cancelled - idempotent success (just log warning)
        _client
            .CancelReversalAsync(Arg.Any<CancelReversalRequest>(),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcFail<CancelReversalResponse>(StatusCode.NotFound, "reversal not found"));

        var ex = await Record.ExceptionAsync(() =>
            Build().CancelRevenueReversalAsync("rev-gone", "reason", CancellationToken.None));

        Assert.Null(ex);
    }
}
