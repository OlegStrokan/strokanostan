using Application.DTOs;

namespace Application.Gateways;

public interface IAccountingGateway
{
    Task<string> ReverseRevenueAsync(
        Guid orderId,
        Guid returnRequestId,
        decimal amount,
        string currency,
        List<OrderItemDto> returnedItems,
        CancellationToken cancellationToken);

    Task CancelRevenueReversalAsync(
        string reversalId,
        string reason,
        CancellationToken cancellationToken);
}