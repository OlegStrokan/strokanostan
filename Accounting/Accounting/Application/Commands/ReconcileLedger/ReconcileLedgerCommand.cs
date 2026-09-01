using Application.Common;
using MediatR;

namespace Application.Commands.ReconcileLedger;

public sealed record ReconcileLedgerCommand(int MaxFindings, long MaxAcceptableLag, bool ReportIncidents = true)
    : IRequest<Result<LedgerReconciliationReport>>;

public sealed record LedgerReconciliationReport(
    bool IsHealthy,
    IReadOnlyList<string> Findings,
    int CurrenciesChecked);
