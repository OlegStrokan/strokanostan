using Application.Common;
using Application.Interfaces;
using Domain.Entities;
using Domain.Interfaces;
using Domain.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Commands.ProjectReportingEntries;

internal sealed class ProjectReportingEntriesCommandHandler(
    ILedgerReportingEntryRepository reportingRepository,
    IFxRateRepository fxRateRepository,
    IUnitOfWork unitOfWork,
    ILogger<ProjectReportingEntriesCommandHandler> logger)
    : IRequestHandler<ProjectReportingEntriesCommand, Result<ReportingProjectionReport>>
{
    public async Task<Result<ReportingProjectionReport>> Handle(
        ProjectReportingEntriesCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ReportingCurrency))
            return Result<ReportingProjectionReport>.Failure("ReportingCurrency is required.");

        var reportingCurrency = request.ReportingCurrency.Trim().ToUpperInvariant();
        var batchSize = request.BatchSize > 0 ? request.BatchSize : 200;

        var pending = await reportingRepository.GetUnprojectedTransactionsAsync(batchSize, cancellationToken);
        if (pending.Count == 0)
            return Result<ReportingProjectionReport>.Success(new ReportingProjectionReport(0, 0, []));

        var projected = new List<LedgerReportingEntry>();
        var missingRates = new HashSet<string>(StringComparer.Ordinal);
        var transactionCount = 0;

        foreach (var transaction in pending)
        {
            var rate = await ResolveRateAsync(transaction, reportingCurrency, cancellationToken);

            if (rate is null)
            {
                // Leaving it unprojected is the right call: the transaction stays in the queue and
                // converts correctly once the rate lands, instead of being booked at a guess.
                missingRates.Add($"{transaction.Currency}->{reportingCurrency}");
                continue;
            }

            var entries = ReportingProjector.Project(
                transaction,
                rate.Value,
                reportingCurrency,
                DateTime.UtcNow);

            if (entries.Count == 0)
                continue;

            projected.AddRange(entries);
            transactionCount++;
        }

        if (projected.Count > 0)
        {
            await reportingRepository.AddRangeAsync(projected, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        if (missingRates.Count > 0)
            logger.LogWarning(
                "Reporting projection skipped transactions with no FX rate: {MissingRates}",
                string.Join(", ", missingRates));

        logger.LogInformation(
            "Reporting projection wrote {EntryCount} entries for {TransactionCount} transactions in {ReportingCurrency}.",
            projected.Count,
            transactionCount,
            reportingCurrency);

        return Result<ReportingProjectionReport>.Success(
            new ReportingProjectionReport(transactionCount, projected.Count, missingRates.ToList()));
    }

    private async Task<decimal?> ResolveRateAsync(
        LedgerTransaction transaction,
        string reportingCurrency,
        CancellationToken cancellationToken)
    {
        if (string.Equals(transaction.Currency, reportingCurrency, StringComparison.Ordinal))
            return 1m;

        var rate = await fxRateRepository.GetEffectiveRateAsync(
            transaction.Currency,
            reportingCurrency,
            transaction.OccurredAt,
            cancellationToken);

        return rate?.Rate;
    }
}
