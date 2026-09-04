using Protos.AdminOps;

namespace OpsConsole.Endpoints;

public static class LedgerEndpoints
{
    public static void MapLedgerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ledger")
            .WithTags("Ledger")
            .RequireAuthorization("OpsViewer");

        group.MapGet("/trial-balance", async (
            AdminAccountingService.AdminAccountingServiceClient client,
            string? currency) =>
        {
            var response = await client.GetTrialBalanceAsync(
                new GetTrialBalanceRequest { ReportingCurrency = currency ?? string.Empty });

            return Results.Ok(new
            {
                reportingCurrency = response.ReportingCurrency,
                isBalanced = response.IsBalanced,
                reportingDebits = ToDecimal(response.ReportingDebits),
                reportingCredits = ToDecimal(response.ReportingCredits),
                transactionCurrencyBalances = response.TransactionCurrencyBalances.Select(Map),
                reportingCurrencyBalances = response.ReportingCurrencyBalances.Select(Map),
            });
        });

        group.MapGet("/orders/{orderId}/money-trail", async (
            string orderId,
            AdminAccountingService.AdminAccountingServiceClient client) =>
        {
            var response = await client.GetOrderMoneyTrailAsync(
                new GetOrderMoneyTrailRequest { OrderId = orderId });

            return Results.Ok(new
            {
                transactions = response.Transactions.Select(t => new
                {
                    transactionId = t.TransactionId,
                    transactionRef = t.TransactionRef,
                    refType = t.RefType,
                    refId = t.RefId,
                    currency = t.Currency,
                    occurredAt = t.OccurredAt,
                    entries = t.Entries.Select(e => new
                    {
                        account = e.Account,
                        direction = e.Direction,
                        amount = ToDecimal(e.Amount),
                        currency = e.Currency,
                    }),
                }),
            });
        });

        group.MapGet("/health", async (AdminAccountingService.AdminAccountingServiceClient client) =>
        {
            // Read-only: the server runs the same checks as the worker but does not page anyone.
            var response = await client.GetLedgerHealthAsync(new GetLedgerHealthRequest());

            return Results.Ok(new
            {
                isHealthy = response.IsHealthy,
                currenciesChecked = response.CurrenciesChecked,
                findings = response.Findings.ToList(),
            });
        });
    }

    private static object Map(AccountBalance balance) => new
    {
        account = balance.Account,
        currency = balance.Currency,
        debits = ToDecimal(balance.Debits),
        credits = ToDecimal(balance.Credits),
        balance = ToDecimal(balance.Balance),
    };

    private static decimal ToDecimal(Protos.Common.DecimalValue? value) =>
        value is null ? 0m : value.Units + (value.Nanos / 1_000_000_000m);
}
