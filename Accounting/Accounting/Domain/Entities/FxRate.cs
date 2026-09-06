namespace Domain.Entities;

// Effective-dated rate: 1 BaseCurrency = Rate QuoteCurrency from EffectiveFrom onwards.
public sealed class FxRate
{
    public string BaseCurrency { get; private set; } = null!;
    public string QuoteCurrency { get; private set; } = null!;
    public decimal Rate { get; private set; }
    public DateTime EffectiveFrom { get; private set; }

    private FxRate()
    {
    }

    public static FxRate Create(string baseCurrency, string quoteCurrency, decimal rate, DateTime effectiveFrom)
    {
        if (string.IsNullOrWhiteSpace(baseCurrency))
            throw new ArgumentException("BaseCurrency is required.", nameof(baseCurrency));

        if (string.IsNullOrWhiteSpace(quoteCurrency))
            throw new ArgumentException("QuoteCurrency is required.", nameof(quoteCurrency));

        if (rate <= 0m)
            throw new ArgumentOutOfRangeException(nameof(rate), "Rate must be positive.");

        return new FxRate
        {
            BaseCurrency = baseCurrency.Trim().ToUpperInvariant(),
            QuoteCurrency = quoteCurrency.Trim().ToUpperInvariant(),
            Rate = rate,
            EffectiveFrom = effectiveFrom,
        };
    }
}
