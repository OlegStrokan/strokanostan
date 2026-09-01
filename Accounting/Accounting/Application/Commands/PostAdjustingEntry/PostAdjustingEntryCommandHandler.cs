using Application.Common;
using Application.Interfaces;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Interfaces;
using Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Commands.PostAdjustingEntry;

// The only mutation an operator can make. It adds a balanced transaction; it can never edit or
// delete an existing one, which is what keeps the ledger worth trusting type shit
internal sealed class PostAdjustingEntryCommandHandler(
    ILedgerTransactionRepository repository,
    IUnitOfWork unitOfWork,
    ILogger<PostAdjustingEntryCommandHandler> logger)
    : IRequestHandler<PostAdjustingEntryCommand, Result<string>>
{
    public async Task<Result<string>> Handle(
        PostAdjustingEntryCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AdjustmentId))
            return Result<string>.Failure("AdjustmentId is required — it is the idempotency key.");

        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result<string>.Failure("A reason is required for a manual adjustment.");

        if (string.IsNullOrWhiteSpace(request.PostedBy))
            return Result<string>.Failure("PostedBy is required for a manual adjustment.");

        if (request.Legs is null || request.Legs.Count < 2)
            return Result<string>.Failure("A manual adjustment needs at least two legs.");

        if (request.Legs.Any(l => l.Amount <= 0m))
            return Result<string>.Failure("Every adjustment leg must have a positive amount.");

        var transactionRef = $"adjustment:{request.AdjustmentId.Trim()}";

        var existing = await repository.GetByTransactionRefAsync(transactionRef, cancellationToken);
        if (existing is not null)
        {
            logger.LogInformation(
                "PostAdjustingEntry is an idempotent no-op. AdjustmentId={AdjustmentId}, TransactionId={TransactionId}",
                request.AdjustmentId,
                existing.Id);

            return Result<string>.Success(existing.Id.ToString());
        }

        LedgerTransaction transaction;
        try
        {
            transaction = LedgerTransaction.ForManualAdjustment(
                request.AdjustmentId,
                request.Currency,
                request.Legs
                    .Select(l => new AdjustmentLeg(l.Account, l.Direction, l.Amount))
                    .ToList(),
                request.Reason,
                request.PostedBy,
                request.OrderId,
                DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is ArgumentException or UnbalancedTransactionException)
        {
            return Result<string>.Failure(ex.Message);
        }

        try
        {
            await repository.AddAsync(transaction, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateLedgerTransactionException)
        {
            var concurrent = await repository.GetByTransactionRefAsync(transactionRef, cancellationToken);
            if (concurrent is not null)
                return Result<string>.Success(concurrent.Id.ToString());
            throw;
        }

        logger.LogWarning(
            "Manual adjustment posted. TransactionId={TransactionId}, AdjustmentId={AdjustmentId}, PostedBy={PostedBy}, Reason={Reason}",
            transaction.Id,
            request.AdjustmentId,
            request.PostedBy,
            request.Reason);

        return Result<string>.Success(transaction.Id.ToString());
    }
}
