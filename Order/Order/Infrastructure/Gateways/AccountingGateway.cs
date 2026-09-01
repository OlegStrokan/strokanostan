using Application.DTOs;
using Application.Gateways;
using Grpc.Core;
using Infrastructure.Extensions;
using Protos.Accounting;

namespace Infrastructure.Gateways;

public class AccountingGateway(
    AccountingService.AccountingServiceClient client,
    IConfiguration configuration,
    ILogger<AccountingGateway> logger) : IAccountingGateway
{
    private Metadata BuildHeaders() =>
        new() { { "x-internal-api-key", configuration["InternalServices:AccountingApiKey"] ?? string.Empty } };

    public async Task<string> ReverseRevenueAsync(
        Guid orderId,
        Guid returnRequestId,
        decimal amount,
        string currency,
        List<OrderItemDto> returnedItems,
        CancellationToken cancellationToken)
    {
        var request = new ReverseRevenueRequest
        {
            OrderId = orderId.ToString(),
            ReturnRequestId = returnRequestId.ToString(),
            Amount = amount.ToDecimalValue(),
            Currency = currency
        };

        request.ReturnedItems.AddRange(returnedItems.Select(i => new AccountingItem
        {
            ProductId = i.ProductId.ToString(),
            Quantity = i.Quantity,
            Price = i.Price.ToDecimalValue(),
            Currency = i.Currency
        }));

        try
        {
            var response = await client.ReverseRevenueAsync(request, BuildHeaders(), cancellationToken: cancellationToken);

            if (!response.Success)
                throw new InvalidOperationException(
                    $"Revenue reversal failed. OrderId={orderId}, Amount={amount} {currency}, Error={response.ErrorMessage}");

            logger.LogInformation(
                "Revenue reversed in accounting. OrderId={OrderId}, ReturnRequestId={ReturnRequestId}, ReversalId={ReversalId}, Amount={Amount} {Currency}",
                orderId,
                returnRequestId,
                response.ReversalId,
                amount,
                currency);

            return response.ReversalId;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            throw new InvalidOperationException(
                $"Order not found in accounting system. OrderId={orderId}. Detail={ex.Status.Detail}");
        }
    }

    public async Task CancelRevenueReversalAsync(
        string reversalId,
        string reason,
        CancellationToken cancellationToken)
    {
        var request = new CancelReversalRequest
        {
            ReversalId = reversalId,
            Reason = reason
        };

        try
        {
            var response = await client.CancelReversalAsync(request, BuildHeaders(), cancellationToken: cancellationToken);

            if (!response.Success)
            {
                throw new InvalidOperationException(
                    $"Canceling revenue reversal failed. ReversalId={reversalId}, Error={response.ErrorMessage}");
            }

            logger.LogInformation(
                "Revenue reversal cancelling in accounting. ReversalId={ReversalId}",
                reversalId);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            // idempotent: already cancelled/not found
            logger.LogWarning(
                "CancelRevenueReversal: reversal not found (treated as idempotent success). ReversalId={ReversalId}",
                reversalId);
        }
    }
    


}