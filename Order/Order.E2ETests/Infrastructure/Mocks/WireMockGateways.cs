using Application.Common.Enums;
using Application.DTOs;
using Application.DTOs.ShipmentGateway;
using Application.Gateways;
using Application.Gateways.Exceptions;
using Grpc.Core;
using Grpc.Net.Client;
using Infrastructure.Extensions;
using Protos.Accounting;
using Protos.Inventory;
using Protos.Payment;
using Protos.Product;
using System.Security.Cryptography;
using System.Text;

namespace Order.E2ETests.Infrastructure.Mocks;

// these are the same gateway implementation of production code


//---------Payment Gateway---------//

public class FakeGrpcPaymentGateway : IPaymentGateway
{
    private readonly PaymentService.PaymentServiceClient _client;

    public FakeGrpcPaymentGateway(string serverAddress)
    {
        var channel = GrpcChannel.ForAddress(serverAddress);
        _client = new PaymentService.PaymentServiceClient(channel);
    }

    public async Task<PaymentProcessingResult> ProcessPaymentAsync(
        Guid orderId, 
        Guid customerId,
        decimal amount,
        string currency, 
        string paymentMethod,
        CancellationToken cancellationToken)
    {
        var normalizedCurrency = NormalizeCurrency(currency);
        var idempotencyKey = BuildIdempotencyKey(
            "grpc-process",
            $"{orderId}|{customerId}|{amount:F4}|{normalizedCurrency}");

        try
        {
            var response = await _client.ProcessPaymentAsync(
                new ProcessPaymentRequest
                {
                    OrderId = orderId.ToString(),
                    CustomerId = customerId.ToString(),
                    Amount = amount.ToDecimalValue(),
                    Currency = normalizedCurrency,
                    PaymentMethod = paymentMethod,
                    IdempotencyKey = idempotencyKey,
                },
                cancellationToken: cancellationToken);

            var status = MapPaymentStatus(response);

            if (status == PaymentProcessingStatus.Failed)
            {
                throw string.IsNullOrWhiteSpace(response.ErrorCode) ? new PaymentDeclinedException(
                    $"[{response.ErrorCode}] {response.ErrorMessage}") : response.ErrorCode switch
                {
                    "INSUFFICIENT_FUNDS" => new InsufficientFundsException(
                        $"Payment failed: insufficient funds. OrderId={orderId}, Amount={amount} {normalizedCurrency}"),
                    "PAYMENT_DECLINED" => new PaymentDeclinedException(
                        $"Payment declined by provider. OrderId={orderId}, Reason={response.ErrorMessage}"),
                    _ => new InvalidOperationException(
                        $"Payment processing failed. OrderId={orderId}, Error={response.ErrorMessage}")
                };
            }

            return new PaymentProcessingResult(
                PaymentId: response.PaymentId,
                Status: status,
                ProviderPaymentIntentId: string.IsNullOrWhiteSpace(response.ProviderPaymentIntentId)
                    ? null
                    : response.ProviderPaymentIntentId,
                ClientSecret: string.IsNullOrWhiteSpace(response.ClientSecret)
                    ? null
                    : response.ClientSecret,
                ErrorCode: string.IsNullOrWhiteSpace(response.ErrorCode)
                    ? null
                    : response.ErrorCode,
                ErrorMessage: string.IsNullOrWhiteSpace(response.ErrorMessage)
                    ? null
                    : response.ErrorMessage);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
        {
            throw new PaymentDeclinedException(
                $"Invalid payment data for OrderId={orderId}. Detail={ex.Status.Detail}");
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.FailedPrecondition)
        {
            throw new InsufficientFundsException(
                $"Payment precondition failed for OrderId={orderId}. Detail={ex.Status.Detail}");
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
        {
            throw new GatewayUnavailableException(
                GatewayUnavailableReason.Timeout,
                $"Payment service deadline exceeded for OrderId={orderId}. gRPC={ex.StatusCode}: {ex.Status.Detail}",
                ex);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
        {
            throw new GatewayUnavailableException(
                GatewayUnavailableReason.ServiceUnavailable,
                $"Payment service unavailable for OrderId={orderId}. gRPC={ex.StatusCode}: {ex.Status.Detail}",
                ex);
        }
    }

    public async Task<PaymentProcessingResult> CaptureAsync(
        Guid orderId,
        Guid customerId,
        string providerPaymentIntentId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken)
    {
        var normalizedCurrency = NormalizeCurrency(currency);
        var idempotencyKey = BuildIdempotencyKey(
            "grpc-capture",
            $"{orderId}|{providerPaymentIntentId}|{amount:F4}|{normalizedCurrency}");

        var response = await _client.CapturePaymentAsync(
            new CapturePaymentRequest
            {
                OrderId = orderId.ToString(),
                CustomerId = customerId.ToString(),
                ProviderPaymentIntentId = providerPaymentIntentId,
                Amount = amount.ToDecimalValue(),
                Currency = normalizedCurrency,
                IdempotencyKey = idempotencyKey,
            },
            cancellationToken: cancellationToken);

        var status = (int)response.Status == 1
            ? PaymentProcessingStatus.Succeeded
            : PaymentProcessingStatus.Failed;

        return new PaymentProcessingResult(
            PaymentId: response.PaymentId,
            Status: status,
            ProviderPaymentIntentId: string.IsNullOrWhiteSpace(response.ProviderPaymentIntentId)
                ? null : response.ProviderPaymentIntentId,
            ErrorCode: string.IsNullOrWhiteSpace(response.ErrorCode) ? null : response.ErrorCode,
            ErrorMessage: string.IsNullOrWhiteSpace(response.ErrorMessage) ? null : response.ErrorMessage);
    }

    public async Task CancelAuthorizationAsync(
        string providerPaymentIntentId,
        CancellationToken cancellationToken)
    {
        await _client.CancelAuthorizationAsync(
            new CancelAuthorizationRequest { ProviderPaymentIntentId = providerPaymentIntentId },
            cancellationToken: cancellationToken);
    }

    private static PaymentProcessingStatus MapPaymentStatus(ProcessPaymentResponse response)
    {
        var rawStatus = (int)response.Status;

        return rawStatus switch
        {
            1 => PaymentProcessingStatus.Succeeded,
            2 => PaymentProcessingStatus.Pending,
            3 => PaymentProcessingStatus.Failed,
            4 => PaymentProcessingStatus.RequiresAction,
            5 => PaymentProcessingStatus.Authorized,
            _ => response.Success
                ? PaymentProcessingStatus.Succeeded
                : PaymentProcessingStatus.Failed,
        };
    }

    public async Task<PaymentProcessingResult> AuthorizeAsync(
        Guid orderId,
        Guid customerId,
        decimal amount,
        string currency,
        string paymentMethod,
        CancellationToken cancellationToken)
    {
        var normalizedCurrency = NormalizeCurrency(currency);
        var idempotencyKey = BuildIdempotencyKey(
            "grpc-authorize",
            $"{orderId}|{customerId}|{amount:F4}|{normalizedCurrency}");

        var response = await _client.ProcessPaymentAsync(
            new ProcessPaymentRequest
            {
                OrderId = orderId.ToString(),
                CustomerId = customerId.ToString(),
                Amount = amount.ToDecimalValue(),
                Currency = normalizedCurrency,
                PaymentMethod = paymentMethod,
                IdempotencyKey = idempotencyKey,
                CaptureMethod = "manual",
            },
            cancellationToken: cancellationToken);

        var status = MapPaymentStatus(response);

        if (status == PaymentProcessingStatus.Failed)
        {
            throw response.ErrorCode switch
            {
                "INSUFFICIENT_FUNDS" => new InsufficientFundsException(
                    $"Authorization failed: insufficient funds. OrderId={orderId}"),
                "PAYMENT_DECLINED" => new PaymentDeclinedException(
                    $"Authorization declined. OrderId={orderId}, Reason={response.ErrorMessage}"),
                _ => new InvalidOperationException(
                    $"Authorization failed. OrderId={orderId}, Error={response.ErrorMessage}")
            };
        }

        return new PaymentProcessingResult(
            PaymentId: response.PaymentId,
            Status: status,
            ProviderPaymentIntentId: string.IsNullOrWhiteSpace(response.ProviderPaymentIntentId) ? null : response.ProviderPaymentIntentId,
            ClientSecret: string.IsNullOrWhiteSpace(response.ClientSecret) ? null : response.ClientSecret,
            ErrorCode: string.IsNullOrWhiteSpace(response.ErrorCode) ? null : response.ErrorCode,
            ErrorMessage: string.IsNullOrWhiteSpace(response.ErrorMessage) ? null : response.ErrorMessage);
    }

    public async Task<string> RefundAsync(
        string paymentId,
        decimal amount,
        string currency,
        string reason,
        CancellationToken cancellationToken)
    {
        var result = await RefundWithStatusAsync(paymentId, amount, currency, reason, cancellationToken);
        return result.RefundId;
    }

    public async Task<RefundProcessingResult> RefundWithStatusAsync(
        string paymentId,
        decimal amount,
        string currency,
        string reason,
        CancellationToken cancellationToken)
    {
        var normalizedCurrency = NormalizeCurrency(currency);
        var idempotencyKey = BuildIdempotencyKey(
            "grpc-refund",
            $"{paymentId}|{amount:F4}|{normalizedCurrency}|{reason}");

        try
        {
            var response = await _client.RefundPaymentAsync(
                new RefundPaymentRequest
                {
                    PaymentId = paymentId,
                    Amount = amount.ToDecimalValue(),
                    Currency = normalizedCurrency,
                    Reason = reason,
                    IdempotencyKey = idempotencyKey,
                },
                cancellationToken: cancellationToken);

            if (!response.Success)
            {
                throw new InvalidOperationException($"Refund failed: {response.ErrorMessage}");
            }

            var status = MapRefundStatus(response);

            if (string.IsNullOrWhiteSpace(response.RefundId))
            {
                throw new InvalidOperationException(
                    $"Payment service returned empty RefundId for non-failed status. PaymentId={paymentId}");
            }

            return new RefundProcessingResult(
                RefundId: response.RefundId,
                Status: status,
                ErrorCode: string.IsNullOrWhiteSpace(response.ErrorCode) ? null : response.ErrorCode,
                ErrorMessage: string.IsNullOrWhiteSpace(response.ErrorMessage) ? null : response.ErrorMessage,
                ProviderRefundId: string.IsNullOrWhiteSpace(response.ProviderRefundId) ? null : response.ProviderRefundId);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            throw new InvalidOperationException(
                $"Payment not found for refund. PaymentId={paymentId}. Detail={ex.Status.Detail}");
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
        {
            throw new GatewayUnavailableException(
                GatewayUnavailableReason.Timeout,
                $"Refund service deadline exceeded for PaymentId={paymentId}. gRPC={ex.StatusCode}: {ex.Status.Detail}",
                ex);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
        {
            throw new GatewayUnavailableException(
                GatewayUnavailableReason.ServiceUnavailable,
                $"Refund service unavailable for PaymentId={paymentId}. gRPC={ex.StatusCode}: {ex.Status.Detail}",
                ex);
        }
    }

    private static RefundProcessingStatus MapRefundStatus(RefundPaymentResponse response)
    {
        var rawStatus = (int)response.Status;

        return rawStatus switch
        {
            1 => RefundProcessingStatus.Succeeded,
            2 => RefundProcessingStatus.Pending,
            3 => throw new InvalidOperationException(
                $"Payment service returned failed refund status for successful response. Error={response.ErrorMessage}"),
            _ => response.Success
                ? RefundProcessingStatus.Succeeded
                : throw new InvalidOperationException(
                    $"Payment service returned unsuccessful refund response. Error={response.ErrorMessage}")
        };
    }

    private static string BuildIdempotencyKey(string prefix, string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return $"{prefix}:{Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant()}";
    }

    private static string NormalizeCurrency(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "USD";
        }

        return value.Trim().ToUpperInvariant();
    }
}

//---------Inventory Gateway---------//

public class FakeGrpcInventoryGateway : IInventoryGateway
{
    private readonly InventoryService.InventoryServiceClient _client;

    public FakeGrpcInventoryGateway(string serverAddress)
    {
        var channel = GrpcChannel.ForAddress(serverAddress);
        _client = new InventoryService.InventoryServiceClient(channel);
    }

    public async Task<string> ReserveAsync(
        Guid orderId,
        List<OrderItemDto> items,
        CancellationToken cancellationToken)
    {
        var request = new ReserveInventoryRequest
        {
            OrderId = orderId.ToString()
        };

        foreach (var item in items)
        {
            request.Items.Add(new InventoryItem
            {
                ProductId = item.ProductId.ToString(),
                Quantity = item.Quantity
            });
        }

        var response = await _client.ReserveInventoryAsync(
            request,
            cancellationToken: cancellationToken);

        if (!response.Success)
            throw new InsufficientExecutionStackException(response.ErrorMessage);

        return response.ReservationId;
    }

    public async Task ReleaseReservationAsync(string reservationId, CancellationToken cancellationToken)
    {
        var response = await _client.ReleaseInventoryAsync(
            new ReleaseInventoryRequest { ReservationId = reservationId },
            cancellationToken: cancellationToken);

        if (!response.Success)
            throw new Exception($"Release failed: {response.ErrorMessage}");
    }

    public async Task ConfirmReservationAsync(string reservationId, CancellationToken cancellationToken)
    {
        var response = await _client.ConfirmReservationAsync(
            new ConfirmReservationRequest { ReservationId = reservationId },
            cancellationToken: cancellationToken);

        if (!response.Success)
            throw new Exception($"Confirm failed: {response.ErrorMessage}");
    }
}

//---------Accounting Gateway---------//

public class FakeGrpcAccountingGateway : IAccountingGateway
{
    private readonly AccountingService.AccountingServiceClient _client;

    public FakeGrpcAccountingGateway(string serverAddress)
    {
        var channel = GrpcChannel.ForAddress(serverAddress);
        _client = new AccountingService.AccountingServiceClient(channel);
    }

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
            Currency = currency,
        };

        foreach (var item in returnedItems)
        {
            request.ReturnedItems.Add(new AccountingItem
            {
                ProductId = item.ProductId.ToString(),
                Quantity = item.Quantity,
                Price = item.Price.ToDecimalValue(),
                Currency = item.Currency
            });
        }

        var response = await _client.ReverseRevenueAsync(
            request,
            cancellationToken: cancellationToken);

        if (!response.Success)
            throw new Exception($"ReverseRevenue failed: {response.ErrorMessage}");

        return response.ReversalId;
    }

    public async Task CancelRevenueReversalAsync(string reversalId, string reason, CancellationToken cancellationToken)
    {
        var response = await _client.CancelReversalAsync(
            new CancelReversalRequest { ReversalId = reversalId, Reason = reason },
            cancellationToken: cancellationToken);

        if (!response.Success)
            throw new Exception($"CancelRevenueReversal failed: {response.ErrorMessage}");
    }
}

public class WireMockShippingGateway : IShippingGateway
{
    private readonly HttpClient _http;

    public WireMockShippingGateway(string wireMockBaseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(wireMockBaseUrl) };
    }

    public async Task<ShipmentResultDto> CreateShipmentAsync(
        Guid orderId,
        AddressDto deliveryAddress,
        IReadOnlyCollection<OrderItemDto> items,
        ShippingCarrier carrier,
        CancellationToken cancellationToken)
    {
        var resp = await _http.PostAsJsonAsync(
            "/api/shipping/create",
            new { orderId, deliveryAddress, items },
            cancellationToken);

        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<ShipmentResponse>(cancellationToken)
                   ?? throw new Exception("Empty shipping response");

        return new ShipmentResultDto(body.ShipmentId, body.TrackingNumber);
    }

    public async Task CancelShipmentAsync(string shipmentId, CancellationToken cancellationToken)
    {
        var resp = await _http.PostAsJsonAsync(
            "/api/shipping/cancel",
            new { shipmentId },
            cancellationToken);

        resp.EnsureSuccessStatusCode();
    }

    // these shit is used by other flows (return saga ...) - throw for now in order e2e tests
    public Task<ShipmentStatusDto> GetShipmentStatusAsync(string trackingNumber, CancellationToken ct)
        => throw new NotImplementedException();

    public async Task<ReturnShipmentResultDto> CreateReturnShipmentAsync(
        Guid orderId,
        Guid customerId,
        List<OrderItemDto> items,
        ShippingCarrier carrier,
        CancellationToken cancellationToken)
    {
        var resp = await _http.PostAsJsonAsync(
            "/api/shipping/return/create",
            new { orderId, customerId, items },
            cancellationToken);

        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<ReturnShipmentResponse>(cancellationToken)
                   ?? throw new Exception("Empty return shipment response");

        return new ReturnShipmentResultDto(
            body.ReturnShipmentId,
            body.ReturnTrackingNumber,
            body.ExpectedPickupDate);
    }

    public async Task RegisterWebhookAsync(
        string shipmentId,
        string callbackUrl,
        string[] events,
        CancellationToken cancellationToken)
    {
        var resp = await _http.PostAsJsonAsync(
            "/api/shipping/webhook/register",
            new { shipmentId, callbackUrl, events }, cancellationToken);

        resp.EnsureSuccessStatusCode();
    }

    public async Task CancelReturnShipmentAsync(
        string returnShipmentId,
        string reason,
        CancellationToken cancellationToken)
    {
        var resp = await _http.PostAsJsonAsync(
            "/api/shipping/return/cancel",
            new { returnShipmentId, reason }, cancellationToken);

        resp.EnsureSuccessStatusCode();
    }

    public Task<ReturnShipmentStatusDto> GetReturnShipmentStatusAsync(
        string returnTrackingNumber, CancellationToken ct)
        => throw new NotImplementedException();

    private record ShipmentResponse(string ShipmentId, string TrackingNumber);

    private record ReturnShipmentResponse(
        string ReturnShipmentId,
        string ReturnTrackingNumber,
        DateTime ExpectedPickupDate);
}

public class FakeGrpcProductGateway : IProductGateway
{
    private readonly ProductService.ProductServiceClient _client;

    public FakeGrpcProductGateway(string serverAddress)
    {
        var channel = GrpcChannel.ForAddress(serverAddress);
        _client = new ProductService.ProductServiceClient(channel);
    }

    public async Task<IReadOnlyList<ProductPriceDto>> GetCurrentPricesAsync(
        IEnumerable<Guid> productIds,
        CancellationToken cancellationToken)
    {
        var request = new GetProductPricesRequest();
        request.ProductIds.AddRange(productIds.Select(id => id.ToString()));

        var response = await _client.GetProductPricesAsync(request, cancellationToken: cancellationToken);

        if (response.NotFoundIds.Count > 0)
            throw new ProductNotFoundException(response.NotFoundIds);

        return response.Prices
            .Select(p => new ProductPriceDto(Guid.Parse(p.ProductId), p.Price.Units + p.Price.Nanos / 1_000_000_000m, p.Currency))
            .ToList();
    }
}

public class FakeUserGateway : IUserGateway
{
    public Task<UserProfileDto> GetUserProfileAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var profile = new UserProfileDto(
            customerId,
            $"{customerId}@e2e.local",
            "E2E Customer",
            "US",
            "Standard",
            true);

        return Task.FromResult(profile);
    }
}
