using System.Globalization;
using Application.Common;
using Application.Common.Enums;
using Application.Gateways;
using Application.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Commands.ReconcileLedger;

// The aggregate check the per-entity workers in Payment and Order structurally cannot do: they
// converge one stuck row at a time and never compare totals, so a second refund that is
// individually well-formed looks correct to every one of them
internal sealed class ReconcileLedgerCommandHandler(
    ILedgerReconciliationReader reader,
    IMoneyEventConsumerMonitor consumerMonitor,
    IIncidentReporter incidentReporter,
    ILogger<ReconcileLedgerCommandHandler> logger)
    : IRequestHandler<ReconcileLedgerCommand, Result<LedgerReconciliationReport>>
{
    private const string AlertType = "ledger_drift";

    public async Task<Result<LedgerReconciliationReport>> Handle(
        ReconcileLedgerCommand request,
        CancellationToken cancellationToken)
    {
        var maxFindings = request.MaxFindings > 0 ? request.MaxFindings : 20;
        var findings = new List<string>();

        var balances = await reader.GetCurrencyBalancesAsync(cancellationToken);
        foreach (var balance in balances.Where(b => !b.IsBalanced))
        {
            findings.Add(
                $"Currency {balance.Currency} is unbalanced: debits {Format(balance.Debits)} vs credits {Format(balance.Credits)} (drift {Format(balance.Drift)}).");
        }

        var unbalanced = await reader.GetUnbalancedTransactionsAsync(maxFindings, cancellationToken);
        foreach (var transaction in unbalanced)
        {
            findings.Add(
                $"Transaction {transaction.TransactionRef} ({transaction.TransactionId}) is unbalanced in {transaction.Currency}: debits {Format(transaction.Debits)} vs credits {Format(transaction.Credits)}.");
        }

        var overRefunded = await reader.GetOverRefundedOrdersAsync(maxFindings, cancellationToken);
        foreach (var order in overRefunded)
        {
            findings.Add(
                $"Order {order.OrderId} was refunded {Format(order.Refunded)} {order.Currency} against captures of {Format(order.Captured)} {order.Currency}: over-refunded by {Format(order.Refunded - order.Captured)}.");
        }

        var consumer = consumerMonitor.TakeSnapshot();

        if (consumer.SkippedSinceLastSnapshot.Count > 0)
        {
            findings.Add(
                $"The consumer committed past {consumer.SkippedSinceLastSnapshot.Count} money event(s) without posting them: {string.Join(", ", consumer.SkippedSinceLastSnapshot.Take(maxFindings))}. The ledger is short those postings until they are replayed.");
        }

        if (request.MaxAcceptableLag > 0 && consumer.Lag > request.MaxAcceptableLag)
        {
            findings.Add(
                $"Money event consumer lag is {consumer.Lag}, above the {request.MaxAcceptableLag} threshold. The ledger is behind Payment.");
        }

        var report = new LedgerReconciliationReport(findings.Count == 0, findings, balances.Count);

        if (report.IsHealthy)
        {
            logger.LogInformation(
                "Ledger reconciliation passed. CurrenciesChecked={CurrencyCount}, ConsumerLag={Lag}",
                balances.Count,
                consumer.Lag);

            return Result<LedgerReconciliationReport>.Success(report);
        }

        logger.LogCritical(
            "Ledger reconciliation found {FindingCount} problem(s): {Findings}",
            findings.Count,
            string.Join(" | ", findings));

        if (!request.ReportIncidents)
            return Result<LedgerReconciliationReport>.Success(report);

        await incidentReporter.ReportAsync(
            new LedgerIncident(
                AlertType,
                AlertSeverity.Critical,
                $"Ledger reconciliation found {findings.Count} problem(s).",
                findings),
            cancellationToken);

        return Result<LedgerReconciliationReport>.Success(report);
    }

    private static string Format(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
