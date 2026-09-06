using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;

namespace Domain.Services;

// Converts one ledger transaction into reporting-currency entries
public static class ReportingProjector
{
    private const int ReportingScale = 4;

    // Every leg of one transaction converts at the same rate, so balance survives the conversion.
    // Only the per-leg rounding can break it, and that residue goes to fx_gain_loss.
    public static IReadOnlyList<LedgerReportingEntry> Project(
        LedgerTransaction transaction,
        decimal rate,
        string reportingCurrency,
        DateTime convertedAt)
    {
        if (rate <= 0m)
            throw new ArgumentOutOfRangeException(nameof(rate), "Rate must be positive.");

        if (string.IsNullOrWhiteSpace(reportingCurrency))
            throw new ArgumentException("Reporting currency is required.", nameof(reportingCurrency));

        var currency = reportingCurrency.Trim().ToUpperInvariant();
        var projected = new List<LedgerReportingEntry>();

        var debits = 0m;
        var credits = 0m;

        foreach (var entry in transaction.Entries)
        {
            var converted = Math.Round(entry.Amount * rate, ReportingScale, MidpointRounding.ToEven);

            // A leg can round away to nothing on a tiny amount and a small rate. Dropping it is
            // correct: a zero posting is not a posting.
            if (converted <= 0m)
                continue;

            if (entry.Direction == EntryDirection.Debit)
                debits += converted;
            else
                credits += converted;

            projected.Add(LedgerReportingEntry.FromEntry(entry, converted, currency, rate, convertedAt));
        }

        if (projected.Count == 0)
            return projected;

        var residual = debits - credits;

        if (residual != 0m)
        {
            projected.Add(LedgerReportingEntry.FxResidual(
                transaction.Id,
                // A debit surplus needs a credit to close it, and the other way round.
                residual > 0m ? EntryDirection.Credit : EntryDirection.Debit,
                Math.Abs(residual),
                currency,
                rate,
                convertedAt));
        }

        EnsureBalanced(transaction, projected);
        return projected;
    }

    private static void EnsureBalanced(
        LedgerTransaction transaction,
        IReadOnlyList<LedgerReportingEntry> projected)
    {
        var debits = projected.Where(e => e.Direction == EntryDirection.Debit).Sum(e => e.ReportingAmount);
        var credits = projected.Where(e => e.Direction == EntryDirection.Credit).Sum(e => e.ReportingAmount);

        if (debits != credits)
            throw new UnbalancedTransactionException(
                $"Reporting projection for {transaction.TransactionRef} is unbalanced: debits={debits}, credits={credits}.");
    }
}
