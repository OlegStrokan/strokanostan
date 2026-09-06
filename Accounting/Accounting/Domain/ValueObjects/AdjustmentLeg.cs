using Domain.Enums;

namespace Domain.ValueObjects;

public sealed record AdjustmentLeg(LedgerAccount Account, EntryDirection Direction, decimal Amount);
