using Domain.Entities;

namespace Domain.Interfaces;

public interface ILedgerReportingEntryRepository
{
    // Transactions with no reporting rows yet, oldest first.
    Task<IReadOnlyList<LedgerTransaction>> GetUnprojectedTransactionsAsync(
        int maxCount,
        CancellationToken cancellationToken = default);

    Task AddRangeAsync(
        IEnumerable<LedgerReportingEntry> entries,
        CancellationToken cancellationToken = default);
}
