using Domain.Enums;

namespace Domain.Entities;

// Derived reporting-currency copy of a ledger entry. Rebuildable. Never writes back
public sealed class LedgerReportingEntry
{
    public Guid Id { get; private set; }
    public Guid TransactionId { get; private set; }

    // Null for the synthetic fx_gain_loss leg, which has no primitive entry behind it
    public Guid? EntryId { get; private set; }

    public LedgerAccount Account { get; private set; }
    public EntryDirection Direction { get; private set; }
    public decimal ReportingAmount { get; private set; }
    public string ReportingCurrency { get; private set; } = null!;
    public decimal RateUsed { get; private set; }
    public DateTime ConvertedAt { get; private set; }

    private LedgerReportingEntry()
    {
    }

    private LedgerReportingEntry(
        Guid transactionId,
        Guid? entryId,
        LedgerAccount account,
        EntryDirection direction,
        decimal reportingAmount,
        string reportingCurrency,
        decimal rateUsed,
        DateTime convertedAt)
    {
        if (reportingAmount <= 0m)
            throw new ArgumentOutOfRangeException(
                nameof(reportingAmount), "Reporting amount must be positive.");

        Id = Guid.NewGuid();
        TransactionId = transactionId;
        EntryId = entryId;
        Account = account;
        Direction = direction;
        ReportingAmount = reportingAmount;
        ReportingCurrency = reportingCurrency;
        RateUsed = rateUsed;
        ConvertedAt = convertedAt;
    }

    public static LedgerReportingEntry FromEntry(
        LedgerEntry entry,
        decimal reportingAmount,
        string reportingCurrency,
        decimal rateUsed,
        DateTime convertedAt) =>
        new(entry.TransactionId, entry.Id, entry.Account, entry.Direction,
            reportingAmount, reportingCurrency, rateUsed, convertedAt);

    public static LedgerReportingEntry FxResidual(
        Guid transactionId,
        EntryDirection direction,
        decimal reportingAmount,
        string reportingCurrency,
        decimal rateUsed,
        DateTime convertedAt) =>
        new(transactionId, null, LedgerAccount.FxGainLoss, direction,
            reportingAmount, reportingCurrency, rateUsed, convertedAt);
}
