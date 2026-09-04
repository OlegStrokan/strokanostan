using System.Text;
using System.Threading.RateLimiting;
using Grpc.Core;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.IdentityModel.Tokens;
using OpsConsole.Auth;
using OpsConsole.Endpoints;
using OpsConsole.Grpc;
using Protos.AdminOps;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<InternalApiKeyInterceptor>();
builder.Services.AddSingleton<OperatorSubjectInterceptor>();

builder.Services.AddGrpcClient<AdminOpsService.AdminOpsServiceClient>(options =>
{
    options.Address = new Uri(builder.Configuration["OrderServiceUrl"]
                             ?? "http://localhost:5224");
}).AddInterceptor<InternalApiKeyInterceptor>().AddInterceptor<OperatorSubjectInterceptor>();

// Phase 6 cross-service correlation: same shared-secret pattern, pointed at
// Payment's and Inventory's own admin gRPC services instead of Order's.
builder.Services.AddGrpcClient<AdminPaymentService.AdminPaymentServiceClient>(options =>
{
    options.Address = new Uri(builder.Configuration["PaymentServiceUrl"]
                             ?? "http://localhost:5080");
}).AddInterceptor<InternalApiKeyInterceptor>().AddInterceptor<OperatorSubjectInterceptor>();

builder.Services.AddGrpcClient<AdminInventoryService.AdminInventoryServiceClient>(options =>
{
    options.Address = new Uri(builder.Configuration["InventoryServiceUrl"]
                             ?? "http://localhost:5074");
}).AddInterceptor<InternalApiKeyInterceptor>().AddInterceptor<OperatorSubjectInterceptor>();

// Accounting's interceptor routes on the service name: this admin surface accepts the OpsConsole
// key, while Order's money-posting API still requires the separate Accounting key.
builder.Services.AddGrpcClient<AdminAccountingService.AdminAccountingServiceClient>(options =>
{
    options.Address = new Uri(builder.Configuration["AccountingServiceUrl"]
                             ?? "http://localhost:5085");
}).AddInterceptor<InternalApiKeyInterceptor>().AddInterceptor<OperatorSubjectInterceptor>();

// JWT auth. Originally (Phase 4) only mutating endpoints required it, with reads
// staying behind ApiKeyMiddleware alone. Phase 7 extends the same JWT + role check to
// read endpoints too ("OpsViewer" policy below) — the shared X-Admin-Api-Key can end
// up copy-pasted into more places than intended, so viewing saga/payment/DLQ data now
// also requires a real operator identity. Tokens are the same ones Auth/Gateway already
// issue (RS256: OpsConsole holds only Auth's PUBLIC key — see Gateway.Api/Program.cs for
// the same verify-only pattern).
var jwtPublicKeyBase64 = builder.Configuration["Jwt:PublicKeyBase64"];
var jwtAudience = builder.Configuration["Jwt:Audience"];
var jwtIssuer = builder.Configuration["Jwt:Issuer"];

// Same startup contract as Gateway.Api/Program.cs: all three must be present outside
// Development and must match Auth's values, or tokens Auth issues get rejected at runtime
// with no indication of why.
if (!builder.Environment.IsDevelopment())
{
    if (string.IsNullOrWhiteSpace(jwtPublicKeyBase64))
        throw new InvalidOperationException("Jwt:PublicKeyBase64 must be configured outside development — Auth's RSA public key.");
    if (string.IsNullOrWhiteSpace(jwtAudience))
        throw new InvalidOperationException("Jwt:Audience must be configured outside development.");
    if (string.IsNullOrWhiteSpace(jwtIssuer))
        throw new InvalidOperationException(
            "Jwt:Issuer must be configured outside development, and must match the issuer Auth signs tokens with.");

    RequireDeployedSecret("AdminApiKey");
    RequireDeployedSecret("InternalServices:OpsConsoleApiKey");
}

// fail loudly if someone deploy prod with placeholder
void RequireDeployedSecret(string key)
{
    var value = builder.Configuration[key];

    if (string.IsNullOrWhiteSpace(value))
        throw new InvalidOperationException($"{key} must be configured outside development.");

    string[] placeholderPrefixes = ["dev_", "replace-me", "change-me"];

    if (placeholderPrefixes.Any(p => value.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        throw new InvalidOperationException(
            $"{key} is still a placeholder. Rotate it before deploying outside development.");
}

RsaSecurityKey? jwtSigningKey = null;
if (!string.IsNullOrWhiteSpace(jwtPublicKeyBase64))
{
    var rsa = System.Security.Cryptography.RSA.Create();
    rsa.ImportFromPem(Encoding.UTF8.GetString(Convert.FromBase64String(jwtPublicKeyBase64)));
    jwtSigningKey = new RsaSecurityKey(rsa);
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = !string.IsNullOrWhiteSpace(jwtAudience),
            ValidAudience = jwtAudience,
            // Issuer validation was previously off here while Gateway had it on — one token
            // format, two policies. Both now key off a configured Jwt:Issuer, which the
            // guard above makes mandatory outside Development.
            ValidateIssuer = !string.IsNullOrWhiteSpace(jwtIssuer),
            ValidIssuer = jwtIssuer,
            ValidateIssuerSigningKey = jwtSigningKey is not null,
            IssuerSigningKey = jwtSigningKey
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { error = "A valid operator access token is required." });
            },
            OnForbidden = async context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { error = "Operator lacks the role required for this action." });
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    // Mutations: unchanged from Phase 4/5.
    options.AddPolicy("OpsAdmin", policy => policy.RequireRole("Admin", "SuperAdmin"));

    // Phase 7 view-level RBAC: reads now also require a real operator identity, not
    // just the shared X-Admin-Api-Key (which could be checked into a config file and
    // shared by many people). "OpsViewer" is included so a future lower-privileged
    // read-only role can be granted via the existing User-service AssignRole flow
    // without touching this policy again — nobody needs to actually hold that role
    // today since Admin/SuperAdmin already satisfy it.
    options.AddPolicy("OpsViewer", policy => policy.RequireRole("Admin", "SuperAdmin", "OpsViewer"));
});

builder.Services.AddRateLimiter(options =>
{
    // The middleware defaults to 503 on rejection, which would be indistinguishable
    // from Order/Payment/Inventory being genuinely unreachable (also mapped to 503
    // below) — 429 makes "you're being throttled" unambiguous to the caller.
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { error = "Too many requests. Please slow down." }, cancellationToken);
    };

    // Mutating saga/DLQ actions: capped independent of the JWT/API-key checks above,
    // so a compromised token or a scripting mistake can't hammer compensation/requeue
    // endlessly. Partitioned by IP, same approach as Gateway's "auth-strict" policy.
    options.AddPolicy("ops-mutation", httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 4,
                PermitLimit = 20,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
            }));
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async httpContext =>
    {
        var feature = httpContext.Features.Get<IExceptionHandlerFeature>();
        if (feature?.Error is RpcException rpcEx)
        {
            (httpContext.Response.StatusCode, var message) = rpcEx.StatusCode switch
            {
                StatusCode.NotFound        => (StatusCodes.Status404NotFound,           rpcEx.Status.Detail),
                StatusCode.InvalidArgument => (StatusCodes.Status400BadRequest,          rpcEx.Status.Detail),
                // PermissionDenied here means Order rejected OpsConsole's internal API
                // key (misconfiguration), not that the operator lacks access — 502 signals
                // an upstream problem rather than blaming the caller with 403.
                StatusCode.PermissionDenied=> (StatusCodes.Status502BadGateway,          "Upstream service rejected the request."),
                StatusCode.Unavailable     => (StatusCodes.Status503ServiceUnavailable,  "Upstream service unavailable."),
                _                          => (StatusCodes.Status500InternalServerError, "An internal error occurred.")
            };
            httpContext.Response.ContentType = "application/json";
            await httpContext.Response.WriteAsJsonAsync(new { error = message });
        }
    });
});

app.UseMiddleware<ApiKeyMiddleware>();

app.MapSagaEndpoints();
app.MapDeadLetterEndpoints();
app.MapSagaMutationEndpoints();
app.MapDeadLetterMutationEndpoints();
app.MapSagaCorrelationEndpoints();
app.MapLedgerEndpoints();
app.MapLedgerMutationEndpoints();
app.MapHealthEndpoints();

app.Run();

// Make Program accessible to WebApplicationFactory in test projects
public partial class Program { }
