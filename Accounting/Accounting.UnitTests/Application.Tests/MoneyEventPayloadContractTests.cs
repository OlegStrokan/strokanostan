using System.Text.Json;
using Application.Common;
using Application.Contracts;

namespace Application.Tests;

// Payment serialises this payload with JsonNamingPolicy.CamelCase. Nothing at compile time
// links the two records, so this test is the contract between the services.
public class MoneyEventPayloadContractTests
{
    private static readonly JsonSerializerOptions ConsumerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private const string PaymentCapturedJson = """
        {
          "eventId": "3fa85f64:captured",
          "eventType": "PaymentCapturedEvent",
          "paymentId": "3fa85f6457174562b3fc2c963f66afa6",
          "orderId": "8a1b2c3d-4e5f-6071-8293-a4b5c6d7e8f9",
          "refundId": null,
          "providerPaymentIntentId": "pi_test_123",
          "amount": 149.99,
          "currency": "EUR",
          "fee": 0,
          "tax": 0,
          "occurredAt": "2026-08-30T09:00:00Z"
        }
        """;

    [Fact]
    public void PaymentsCamelCaseJson_ShouldDeserialiseIntoEveryField()
    {
        var payload = JsonSerializer.Deserialize<MoneyEventPayload>(PaymentCapturedJson, ConsumerOptions);

        Assert.NotNull(payload);
        Assert.Equal("3fa85f64:captured", payload!.EventId);
        Assert.Equal(MoneyEventTypes.PaymentCaptured, payload.EventType);
        Assert.Equal("3fa85f6457174562b3fc2c963f66afa6", payload.PaymentId);
        Assert.Equal("8a1b2c3d-4e5f-6071-8293-a4b5c6d7e8f9", payload.OrderId);
        Assert.Null(payload.RefundId);
        Assert.Equal("pi_test_123", payload.ProviderPaymentIntentId);
        Assert.Equal(149.99m, payload.Amount);
        Assert.Equal("EUR", payload.Currency);
        Assert.Equal(0m, payload.Fee);
        Assert.Equal(0m, payload.Tax);
        Assert.Equal(new DateTime(2026, 8, 30, 9, 0, 0, DateTimeKind.Utc), payload.OccurredAt.ToUniversalTime());
    }

    [Fact]
    public void PaymentIdWithoutDashes_ShouldStillParseAsGuid()
    {
        // Payment mints ids with Guid.NewGuid().ToString("N"), so the ledger's uuid columns
        // depend on the dash-less form parsing.
        Assert.True(Guid.TryParse("3fa85f6457174562b3fc2c963f66afa6", out _));
    }

    [Theory]
    [InlineData(MoneyEventTypes.PaymentAuthorized, "PaymentAuthorizedEvent")]
    [InlineData(MoneyEventTypes.PaymentVoided, "PaymentVoidedEvent")]
    [InlineData(MoneyEventTypes.PaymentCaptured, "PaymentCapturedEvent")]
    [InlineData(MoneyEventTypes.RefundIssued, "RefundIssuedEvent")]
    public void EventTypeNames_ShouldMatchThePaymentSideConstants(string actual, string expected)
    {
        Assert.Equal(expected, actual);
    }
}
