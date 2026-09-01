using Application.Common;
using MediatR;

namespace Application.Commands.ProjectReportingEntries;

public sealed record ProjectReportingEntriesCommand(string ReportingCurrency, int BatchSize)
    : IRequest<Result<ReportingProjectionReport>>;

public sealed record ReportingProjectionReport(
    int TransactionsProjected,
    int EntriesWritten,
    IReadOnlyList<string> MissingRateCurrencies);
