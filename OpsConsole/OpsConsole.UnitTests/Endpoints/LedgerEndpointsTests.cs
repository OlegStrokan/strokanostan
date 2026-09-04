using System.Net;
using System.Net.Http.Json;
using Grpc.Core;
using NSubstitute;
using OpsConsole.UnitTests.TestHelpers;
using Protos.AdminOps;
using static OpsConsole.UnitTests.TestHelpers.GrpcTestHelpers;

namespace OpsConsole.UnitTests.Endpoints;

public class LedgerEndpointsTests : IClassFixture<OpsConsoleWebApplicationFactory>
{
    private readonly OpsConsoleWebApplicationFactory _factory;

    public LedgerEndpointsTests(OpsConsoleWebApplicationFactory factory) => _factory = factory;

    private static Protos.Common.DecimalValue Money(long units) =>
        new() { Units = units, Nanos = 0 };

    [Fact]
    public async Task TrialBalance_ShouldReturn401_WhenNotSignedIn()
    {
        using var client = _factory.CreateClientWithoutJwt();

        var response = await client.GetAsync("/api/ledger/trial-balance");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TrialBalance_ShouldBeReadableByAnOpsViewer()
    {
        // Reads sit on the OpsViewer policy, not OpsAdmin: looking at the books is not a mutation.
        _factory.AccountingClient
            .GetTrialBalanceAsync(Arg.Any<GetTrialBalanceRequest>(), Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcCall(new GetTrialBalanceResponse
            {
                ReportingCurrency = "USD",
                IsBalanced = true,
                ReportingDebits = Money(110),
                ReportingCredits = Money(110),
            }));

        using var client = _factory.CreateAuthorizedClient("OpsViewer");

        var response = await client.GetAsync("/api/ledger/trial-balance");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LedgerHealth_ShouldSurfaceFindings()
    {
        _factory.AccountingClient
            .GetLedgerHealthAsync(Arg.Any<GetLedgerHealthRequest>(), Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcCall(new GetLedgerHealthResponse
            {
                IsHealthy = false,
                CurrenciesChecked = 1,
                Findings = { "Order abc was over-refunded by 4180." },
            }));

        using var client = _factory.CreateAuthorizedClient("Admin");

        var response = await client.GetAsync("/api/ledger/health");
        var body = await response.Content.ReadFromJsonAsync<LedgerHealthBody>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(body!.IsHealthy);
        Assert.Contains("over-refunded", Assert.Single(body.Findings));
    }

    [Fact]
    public async Task MoneyTrail_ShouldReturnTheTransactionsForAnOrder()
    {
        var transaction = new MoneyTrailTransaction
        {
            TransactionId = Guid.NewGuid().ToString(),
            TransactionRef = "capture:pay-1",
            RefType = "Capture",
            Currency = "USD",
        };
        transaction.Entries.Add(new MoneyTrailEntry
        {
            Account = "CustomerCaptured",
            Direction = "Debit",
            Amount = Money(100),
            Currency = "USD",
        });

        _factory.AccountingClient
            .GetOrderMoneyTrailAsync(Arg.Any<GetOrderMoneyTrailRequest>(), Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcCall(new GetOrderMoneyTrailResponse { Transactions = { transaction } }));

        using var client = _factory.CreateAuthorizedClient("Admin");

        var response = await client.GetAsync($"/api/ledger/orders/{Guid.NewGuid()}/money-trail");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed record LedgerHealthBody(bool IsHealthy, int CurrenciesChecked, List<string> Findings);
}
