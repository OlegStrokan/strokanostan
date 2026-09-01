using Application.Common;
using Domain.Enums;
using MediatR;

namespace Application.Commands.PostAdjustingEntry;

public sealed record AdjustingEntryLeg(LedgerAccount Account, EntryDirection Direction, decimal Amount);

public sealed record PostAdjustingEntryCommand(
    string AdjustmentId,
    string Currency,
    IReadOnlyList<AdjustingEntryLeg> Legs,
    string Reason,
    string PostedBy,
    Guid? OrderId) : IRequest<Result<string>>;
