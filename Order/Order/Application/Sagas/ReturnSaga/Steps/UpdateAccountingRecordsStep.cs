using Application.Gateways;
using Application.Sagas.Steps;
using Microsoft.Extensions.Logging;

namespace Application.Sagas.ReturnSaga.Steps;

public sealed class UpdateAccountingRecordsStep(
    IAccountingGateway accountingGateway,
    ILogger<UpdateAccountingRecordsStep> logger) : ISagaStep<ReturnSagaData, ReturnSagaContext>
{
    public string StepName => ReturnSagaSteps.UpdateAccountingRecords;
    public int Order => 5;

    public async Task<StepOutcome> ExecuteAsync(
        ReturnSagaData data,
        ReturnSagaContext context,
        CancellationToken cancellationToken)
    {
        try
        {
           if (string.IsNullOrEmpty(context.RefundId))
            {
                return new Fail("RefundId is required but was not found in context");
            }

            if (data.ReturnRequestId == Guid.Empty)
            {
                return new Fail("ReturnRequestId is required but was not found in saga data");
            }

            logger.LogInformation(
                "Reversing revenue for order {OrderId}, return request {ReturnRequestId}",
                data.CorrelationId,
                data.ReturnRequestId);

            // Only the return-specific leg belongs here: Payment cannot know goods came back.
            var reversalId = await accountingGateway.ReverseRevenueAsync(
                orderId: data.CorrelationId,
                returnRequestId: data.ReturnRequestId,
                amount: data.RefundAmount,
                currency: data.Currency,
                returnedItems: data.ReturnedItems,
                cancellationToken);

            context.RevenueReversalId = reversalId;

            logger.LogInformation(
                "Revenue reversed in accounting. Reversal ID: {ReversalId}",
                reversalId);

            return new Completed(new Dictionary<string, object>
            {
                ["RevenueReversalId"] = reversalId,
                ["Amount"] = data.RefundAmount
            });
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to update accounting records for order {OrderId}",
                data.CorrelationId);

            return new Fail($"Accounting update failed: {ex.Message}");
        }
    }

    public async Task CompensateAsync(
        ReturnSagaData data,
        ReturnSagaContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(context.RevenueReversalId))
        {
            logger.LogInformation(
                "No revenue reversal to cancel for order {OrderId}",
                data.CorrelationId);
            return;
        }

        try
        {
            logger.LogInformation(
                "Cancelling revenue reversal {ReversalId} for order {OrderId}",
                context.RevenueReversalId,
                data.CorrelationId);

            await accountingGateway.CancelRevenueReversalAsync(
                reversalId: context.RevenueReversalId,
                reason: "Return saga compensation - return cancelled",
                cancellationToken);
            
            logger.LogInformation(
                "Successfully cancelled revenue reversal {ReversalId}",
                context.RevenueReversalId);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to cancel revenue reversal {ReversalId}. " +
                "Manual accounting adjustment may be required.",
                context.RevenueReversalId);
        }
    }
}