using System.Net;
using System.Net.Http.Json;
using Grpc.Core;
using NSubstitute;
using OpsConsole.UnitTests.TestHelpers;
using Protos.AdminOps;
using static OpsConsole.UnitTests.TestHelpers.GrpcTestHelpers;

namespace OpsConsole.UnitTests.Endpoints;

public class LedgerMutationEndpointsTests : IClassFixture<OpsConsoleWebApplicationFactory>
{
    private readonly OpsConsoleWebApplicationFactory _factory;

    public LedgerMutationEndpointsTests(OpsConsoleWebApplicationFactory factory) => _factory = factory;

    private static object ValidBody(string? reason = "correcting a mis-post") => new
    {
        adjustmentId = Guid.NewGuid().ToString(),
        currency = "USD",
        reason,
        legs = new[]
        {
            new { account = "CustomerCaptured", direction = "Debit", amount = 10m },
            new { account = "MerchantRevenue", direction = "Credit", amount = 10m },
        },
    };

    [Fact]
    public async Task PostAdjustment_ShouldReturn403_WhenOperatorIsOpsViewerOnly()
    {
        // Viewing the books is not the same right as writing to them.
        using var client = _factory.CreateAuthorizedClient("OpsViewer");

        var response = await client.PostAsJsonAsync("/api/ledger/adjustments", ValidBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostAdjustment_ShouldReturn401_WhenNotSignedIn()
    {
        using var client = _factory.CreateClientWithoutJwt();

        var response = await client.PostAsJsonAsync("/api/ledger/adjustments", ValidBody());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostAdjustment_ShouldRejectAMissingReason()
    {
        // An unexplained manual entry is exactly what makes a ledger untrustworthy.
        using var client = _factory.CreateAuthorizedClient("Admin");

        var response = await client.PostAsJsonAsync("/api/ledger/adjustments", ValidBody(reason: " "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostAdjustment_ShouldRejectFewerThanTwoLegs()
    {
        using var client = _factory.CreateAuthorizedClient("Admin");

        var response = await client.PostAsJsonAsync("/api/ledger/adjustments", new
        {
            adjustmentId = Guid.NewGuid().ToString(),
            currency = "USD",
            reason = "one-sided",
            legs = new[] { new { account = "CustomerCaptured", direction = "Debit", amount = 10m } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostAdjustment_ShouldSendTheOperatorFromTheToken_NotTheBody()
    {
        // Otherwise an operator could attribute the entry to someone else.
        PostAdjustingEntryRequest? sent = null;

        _factory.AccountingClient
            .PostAdjustingEntryAsync(Arg.Do<PostAdjustingEntryRequest>(r => sent = r),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcCall(new PostAdjustingEntryResponse { Success = true, TransactionId = "tx-1" }));

        using var client = _factory.CreateAuthorizedClient("Admin");

        var response = await client.PostAsJsonAsync("/api/ledger/adjustments", ValidBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(sent?.PostedBy));
        Assert.Equal("correcting a mis-post", sent!.Reason);
    }

    [Fact]
    public async Task PostAdjustment_ShouldReturn409_WhenAccountingRejectsIt()
    {
        _factory.AccountingClient
            .PostAdjustingEntryAsync(Arg.Any<PostAdjustingEntryRequest>(), Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcCall(new PostAdjustingEntryResponse
            {
                Success = false,
                ErrorMessage = "Transaction adjustment:x is unbalanced for USD.",
            }));

        using var client = _factory.CreateAuthorizedClient("Admin");

        var response = await client.PostAsJsonAsync("/api/ledger/adjustments", ValidBody());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
