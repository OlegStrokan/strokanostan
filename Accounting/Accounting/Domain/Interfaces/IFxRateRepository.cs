using Domain.Entities;

namespace Domain.Interfaces;

public interface IFxRateRepository
{
    // Latest rate effective at or before asOf. Same-currency conversion needs no row.
    Task<FxRate?> GetEffectiveRateAsync(
        string baseCurrency,
        string quoteCurrency,
        DateTime asOf,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        string baseCurrency,
        string quoteCurrency,
        DateTime effectiveFrom,
        CancellationToken cancellationToken = default);

    Task AddAsync(FxRate rate, CancellationToken cancellationToken = default);
}
