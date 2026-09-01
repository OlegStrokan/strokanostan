using Application.Sagas;
using Application.Sagas.Persistence;
using Application.Sagas.ReturnSaga;
using Confluent.Kafka;
using Domain.Common;
using Domain.Entities.RequestReturn;
using Domain.ValueObjects;
using FluentAssertions;
using Infrastructure.Extensions;
using Order.E2ETests.Infrastructure;
using Protos.Order;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;
using Xunit.Abstractions;
using Address = Protos.Order.Address;
using OrderItem = Protos.Order.OrderItem;

namespace Order.E2ETests.Tests;

/// <summary>
/// E2E tests for Return Request flow.
/// 
/// Flow:
/// 1. RequestReturn gRPC => ReturnRequestCreatedEvent
/// 2. ReturnSaga starts: Validate → AwaitReturnShipment (webhook) => [PAUSE: WaitingForEvent]
/// 3. Webhook: ReturnShipmentDeliveredEvent => Resume saga
/// 4. ConfirmReturnReceived => ProcessRefund => UpdateAccounting → CompleteReturn
/// 
/// Tests:
/// - Happy path with webhook continuation
/// - Idempotency
/// - Compensation when refund fails
/// - Webhook timeout recovery via WatchdogService
/// </summary>
[Collection("E2E")]
public class RequestReturnE2ETests : IClassFixture<E2ETestServer>, IAsyncLifetime
{
    private readonly E2ETestServer _server;
    private readonly ITestOutputHelper _output;
    private OrderService.OrderServiceClient _client = null!;

    public RequestReturnE2ETests(E2ETestServer server, ITestOutputHelper output)
    {
        _server = server;
        _output = output;
    }

    public async Task InitializeAsync()
    {
        await _server.MigrateDatabaseAsync();
        _client = _server.CreateOrderClient();
        _server.ResetAll();
    }

    public Task DisposeAsync() => Task.CompletedTask;
    
    [Fact]
    public async Task RequestReturn_HappyPath_WithWebhookContinuation_CompletesSuccessfully()
    {
        _output.WriteLine("🧪 Return Happy Path - Full flow with webhook continuation");

        var (orderId, productId) = await CreateCompletedOrderAsync();
        _output.WriteLine($"✅ Setup: Order {orderId} completed");
        
        var returnShipmentId = "ReturnShipmentId";
        var returnTrackingNumber = "ReturnTrackingNumber";
        
        _server.ShipmentServer
            .Given(Request.Create().WithPath("/api/shipping/return/create").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBodyAsJson(new 
                { 
                    returnShipmentId, 
                    returnTrackingNumber,
                    labelUrl = "https://shipping.example.com/label/RSHIP-HAPPY-001.pdf"
                }));

        _server.ShipmentServer
            .Given(Request.Create().WithPath("/api/shipping/webhook/register").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));

        var refundId = "RefundId";
        _server.PaymentService.RefundShouldSucceed = true;
        _server.PaymentService.RefundIdToReturn = refundId;

        _server.AccountingServer.ShouldSucceed = true;
        _server.AccountingServer.TransactionIdToReturn = "TransactionId";
        _server.AccountingServer.ReversalIdToReturn = "ReversalId";
        
        var returnRequest = new RequestReturnRequest
        {
            OrderId = orderId.ToString(),
            Reason = "Product defective - not as described",
            IdempotencyKey = $"return-{Guid.NewGuid()}"
        };

        returnRequest.ItemsToReturn.Add(new OrderItem
        {
            ProductId = productId.ToString(),
            Quantity = 1,
            Price = 29.99m.ToDecimalValue(),
            Currency = "USD"
        });

        _output.WriteLine("📤 Requesting return...");
        var returnResponse = await _client.RequestReturnAsync(returnRequest);

        returnResponse.Success.Should().BeTrue("gRPC call must succeed");

        var returnRequestId = Guid.Parse(returnResponse.ReturnRequestId);
        _output.WriteLine($"✅ Return request accepted: {returnRequestId}");

        // Wait for: Outbox → Kafka → ReturnSaga starts
        await Task.Delay(TimeSpan.FromSeconds(5));

        // waiting for webhook 
        var saga = await _server.GetSagaStateAsync(orderId, SagaTypes.ReturnSaga);
        
        saga.Should().NotBeNull("saga must be created");
        saga!.Status.Should().Be(SagaStatus.WaitingForEvent,
            "saga should pause after AwaitReturnShipment step");
        saga.CurrentStep.Should().Be(ReturnSagaSteps.AwaitReturnShipment);

        _output.WriteLine($"✅ Saga paused: Status={saga.Status}, Step={saga.CurrentStep}");

        _server.ShipmentServer.LogEntries.Should().ContainSingle(
            e => e.RequestMessage.Path == "/api/shipping/return/create");
        _server.ShipmentServer.LogEntries.Should().ContainSingle(
            e => e.RequestMessage.Path == "/api/shipping/webhook/register");

        // simulate webhook
        _output.WriteLine("📦 Simulating webhook: Return shipment delivered...");
        
        await PublishReturnShipmentDeliveredEventToKafkaAsync(
            orderId,
            returnShipmentId,
            returnTrackingNumber,
            deliveredAt: DateTime.UtcNow);

        // Wait for: Kafka → SagaContinuationEventHandler → Resume saga
        await Task.Delay(TimeSpan.FromSeconds(10));
        
        saga = await _server.WaitForSagaStatusAsync(
            orderId, SagaTypes.ReturnSaga, SagaStatus.Completed, timeoutSeconds: 30);

        saga.Should().NotBeNull("saga must complete after webhook");
        saga!.Status.Should().Be(SagaStatus.Completed);

        _output.WriteLine($"✅ Saga completed: {saga.Status}");

        // all steps are completed
        var steps = await _server.GetSagaStepLogsAsync(saga.Id);

        var expectedSteps = new[]
        {
            ReturnSagaSteps.ValidateReturnRequest,
            ReturnSagaSteps.AwaitReturnShipment,
            ReturnSagaSteps.ConfirmReturnReceived,
            ReturnSagaSteps.ProcessRefund,
            ReturnSagaSteps.UpdateAccountingRecords,
            ReturnSagaSteps.CompleteReturn
        };

        steps.Select(s => s.StepName).Should().BeEquivalentTo(expectedSteps);
        steps.Should().OnlyContain(s => s.Status == StepStatus.Completed);

        _output.WriteLine($"✅ All {steps.Count} steps completed");

        // check grpc servers 
        _server.PaymentService.RefundCalls.Should().HaveCount(1);
        _server.PaymentService.RefundCalls[0].Amount.ToDecimal().Should().BeApproximately(29.99m, 0.01m);
        _server.PaymentService.RefundCalls[0].Reason.Should().Contain("Product defective");

        // The cash leg belongs to Payment: its RefundIssuedEvent is what posts refund:{refundId}
        // to the ledger. The saga recording it too would double-book.
        _server.AccountingServer.RecordRefundCalls.Should().BeEmpty();

        _server.AccountingServer.ReverseRevenueCalls.Should().HaveCount(1);
        _server.AccountingServer.ReverseRevenueCalls[0].Amount.ToDecimal().Should().BeApproximately(29.99m, 0.01m);

        _output.WriteLine("✅ Payment & Accounting gRPC calls verified");

        // check if aggregate in correct state 
        var events = await _server.GetEventsAsync(returnRequestId, AggregateTypes.ReturnRequest);
        var eventTypes = events.Select(e => e.GetType().Name).ToList();

        eventTypes.Should().Contain("ReturnRequestCreatedEvent");
        eventTypes.Should().Contain("ReturnItemsReceivedEvent");
        eventTypes.Should().Contain("ReturnRefundProcessedEvent");
        eventTypes.Should().Contain("ReturnCompletedEvent");

        _output.WriteLine($"✅ ReturnRequest events: {string.Join(", ", eventTypes)}");

        // Rebuild aggregate from events
        var returnRequestAggregate = RequestReturn.FromHistory(events);
        returnRequestAggregate.Status.Should().Be(ReturnStatus.Completed);
        returnRequestAggregate.RefundId.Should().Be(refundId);

        _output.WriteLine($"✅ ReturnRequest aggregate: Status={returnRequestAggregate.Status}");

        // check if read model is synced 
        var returnReadModel = await _server.WaitForReturnReadModelStatusAsync(
            returnRequestId, "Completed", timeoutSeconds: 30);

        returnReadModel.Should().NotBeNull();
        returnReadModel!.Status.Should().Be("Completed");
        returnReadModel.OrderId.Should().Be(orderId);
        returnReadModel.RefundAmount.Should().Be(29.99m);

        _output.WriteLine($"✅ Read model: Status={returnReadModel.Status}");

        _output.WriteLine("🎉 PASSED: Full return flow with webhook continuation!");
    }

    [Fact]
    public async Task ReqeustReturn_Idempotency_DuplicateRequestReturnsSameReturnRequestId()
    {
        _output.WriteLine("Return Idempotency - Duplicate request");
        
        var (orderId, productId) = await CreateCompletedOrderAsync();
        
        _server.ShipmentServer
            .Given(Request.Create().WithPath("/api/shipping/return/create").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new { returnShipmentId = "returnShipmentId", returnTrackingNumber = "returnTrackingNumber" }));

        _server.ShipmentServer
            .Given(Request.Create().WithPath("/api/shipping/webhook/register").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));
        
        // same idempotency key on both request
        var idempotencyKey = $"return-item-{Guid.NewGuid()}";
        
        var request = new RequestReturnRequest
        {
            OrderId = orderId.ToString(),
            Reason = "Change my mind",
            IdempotencyKey = idempotencyKey
        };
        
        request.ItemsToReturn.Add(new OrderItem
        {
            ProductId = productId.ToString(),
            Quantity = 1,
            Price = 50.00m.ToDecimalValue(),
            Currency = "USD"
        });
        
        _output.WriteLine("Request #1");
        var response1 = await _client.RequestReturnAsync(request);
        
        _output.WriteLine("Request #2");
        var response2 = await _client.RequestReturnAsync(request);

        response1.Success.Should().BeTrue();
        response2.Success.Should().BeTrue();
        response1.ReturnRequestId.Should().Be(response2.ReturnRequestId, "duplicate request must return same returnRequestId");
        
        _output.WriteLine("Both returned: {response1.ReturnRequestId}");

        var returnRequestId = Guid.Parse(response1.ReturnRequestId);
        var events = await _server.GetEventsAsync(returnRequestId, AggregateTypes.ReturnRequest);

        events.Count(e => e.GetType().Name == "ReturnRequestCreatedEvent")
            .Should().Be(1, "only ONE ReturnRequestCreatedEvent must exists");
        
        _output.WriteLine("PASSED: Idempotency work");
    }

    // return fails => saga compensates
    [Fact]
    public async Task RequestReturn_RefundFails_SagaCompensatesAndCancelsReturn()
    {
        _output.WriteLine("Return Rainy Day - Refund failure => compensation");

        var (orderId, productId) = await CreateCompletedOrderAsync();

        var returnShipmentId = "returnShipmentId";
        
        _server.ShipmentServer
            .Given(Request.Create().WithPath("/api/shipping/return/create").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new { returnShipmentId, returnTrackingNumber = "RTRK-FAIL" }));

        _server.ShipmentServer
            .Given(Request.Create().WithPath("/api/shipping/webhook/register").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));

        // shipmentServer: CancelReturnShipment (for compensation)
        _server.ShipmentServer
            .Given(Request.Create().WithPath("/api/shipping/return/cancel").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));
        
        // refund fails
        _server.PaymentService.RefundShouldSucceed = false;
        _server.PaymentService.ErrorMessage = "Payment gateway timeout";

        var request = new RequestReturnRequest
        {
            OrderId = orderId.ToString(),
            Reason = "Product damaged",
            IdempotencyKey = $"return-fail-{Guid.NewGuid()}"
        };
        
        request.ItemsToReturn.Add(new OrderItem
        {
            ProductId = productId.ToString(),
            Quantity = 1, 
            Price = 75.00m.ToDecimalValue(),
            Currency = "USD"
        });
        
        _output.WriteLine("Requesting return...");
        var response = await _client.RequestReturnAsync(request);
        response.Success.Should().BeTrue();

        var returnRequestId = Guid.Parse(response.ReturnRequestId);
        _output.WriteLine($"Return request accepted: {returnRequestId}");

        await Task.Delay(TimeSpan.FromSeconds(5));
        
        // trigger webhook to resume saga (it will fail at processRefund)
        _output.WriteLine("Simulating webhook...");

        await PublishReturnShipmentDeliveredEventToKafkaAsync(
            orderId, returnShipmentId, "TrackingNumber", DateTime.UtcNow);

        await Task.Delay(TimeSpan.FromSeconds(10));

        var saga = await _server.WaitForSagaStatusAsync(
            orderId, SagaTypes.ReturnSaga, SagaStatus.Compensated, timeoutSeconds: 30);

        saga.Should().NotBeNull();
        saga!.Status.Should().Be(SagaStatus.Compensated);
        
        _output.WriteLine($"Saga compensated: {saga.Status}");
        
        
        // check steps: processRefund failed, earlier steps Compensated

        var steps = await _server.GetSagaStepLogsAsync(saga.Id);

        steps.Single(s => s.StepName == ReturnSagaSteps.ProcessRefund)
            .Status.Should().Be(StepStatus.Failed);
        steps.Single(s => s.StepName == ReturnSagaSteps.ConfirmReturnReceived)
            .Status.Should().Be(StepStatus.Compensated);
        steps.Single(s => s.StepName == ReturnSagaSteps.AwaitReturnShipment)
            .Status.Should().Be(StepStatus.Compensated);
        
        _output.WriteLine("ProcessRefund=Failed, earlier steps=Compensated");

        _server.ShipmentServer.LogEntries.Should()
            .ContainSingle(e => e.RequestMessage.Path == "/api/shipping/return/cancel",
                "return shipment must be cancelled during compensation");
        
        _output.WriteLine("Return shipment cancelled via REST");

        var events = await _server.GetEventsAsync(returnRequestId, AggregateTypes.ReturnRequest);
        var eventTypes = events.Select(e => e.GetType().Name).ToList();

        eventTypes.Should().Contain("ReturnRequestCreatedEvent");
        eventTypes.Should().Contain("ReturnItemsReceivedEvent");
        eventTypes.Should().NotContain("ReturnRefundProcessedEvent");
        eventTypes.Should().NotContain("RefundCompletedEvents");
        
        _output.WriteLine("ReturnRequest did not complete (no refund events)");
        _output.WriteLine("Passed: Refund failure compensation worked!");
    }

    [Fact]
    public async Task RequestReturn_WebhookNeverArrives_WatchdogFailsAndCompensatesSaga()
    {
        _output.WriteLine("Return Disaster - Webhook timeout, watchdog compensates");

        var (orderId, productId) = await CreateCompletedOrderAsync();
        
        // shipping success, but webhook never arrives
        _server.ShipmentServer
            .Given(Request.Create().WithPath("/api/shipping/return/create").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new { returnShipmentId = "ShipmentId", returnTrackingNumber = "TrackingNumber" }));

        _server.ShipmentServer
            .Given(Request.Create().WithPath("/api/shipping/webhook/register").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));

        _server.ShipmentServer
            .Given(Request.Create().WithPath("/api/shipping/return/cancel").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));

        var request = new RequestReturnRequest
        {
            OrderId = orderId.ToString(),
            Reason = "Wrong size",
            IdempotencyKey = $"return-timeout-{Guid.NewGuid()}"
        };

        request.ItemsToReturn.Add(new OrderItem
        {
            ProductId = productId.ToString(),
            Quantity = 1,
            Price = 100.00m.ToDecimalValue(),
            Currency = "USD"
        });
        
        _output.WriteLine("Requesting return...");
        var response = await _client.RequestReturnAsync(request);
        response.Success.Should().BeTrue();

        _output.WriteLine("Return request accepted");
        await Task.Delay(TimeSpan.FromSeconds(5));
        
        // saga stuck in waitingForEvent
        var saga = await _server.GetSagaStateAsync(orderId, SagaTypes.ReturnSaga);
        saga.Should().NotBeNull();
        saga!.Status.Should().Be(SagaStatus.WaitingForEvent);
        
        _output.WriteLine($"Saga waiting for webhook: {saga.Status}");
        
        // wait for sagaWatchdogService to detect and compensate ( runs every minute, mark stuck saga after 5 minutes)
        // for testing, we manually trigger watchdog logic (update updatedAt field)
        
        _output.WriteLine("Simulating watchdog scenario: move saga to Running with old timestamp");
        _output.WriteLine("Watchdog only picks up Running sagas (WaitingForEvent is intentionally paused)");

        saga.Status = SagaStatus.Running;
        saga.UpdatedAt = DateTime.UtcNow.AddMinutes(-11);
        await _server.SaveSagaStateAsync(saga);

        // Wait for watchdog cycle to detect and compensate the stuck saga (runs every 1 min)
        _output.WriteLine("Waiting for watchdog to detect and compensate stuck saga...");
        
        saga = await WaitForSagaStatusAsync(orderId, SagaTypes.ReturnSaga,
            s => s == SagaStatus.Compensated || s == SagaStatus.Failed,
            timeoutSeconds: 90);
        
        saga.Should().NotBeNull("watchdog should detect and process stuck saga within timeout");
       (saga!.Status == SagaStatus.Compensated || saga.Status == SagaStatus.Failed)
            .Should().BeTrue("watchdog should fail and compensate stuck saga");
        
        _output.WriteLine($"Watchdog recovered: {saga.Status}");

       _server.ShipmentServer.LogEntries.Should()
            .ContainSingle(e => e.RequestMessage.Path == "/api/shipping/return/cancel");
        
        _output.WriteLine("PASSED: Watchdog recovered stuck saga");
        

    }
    
    // starting point — returns (orderId, productId) so tests can build valid return requests
    private async Task<(Guid orderId, Guid productId)> CreateCompletedOrderAsync()
    {
        // Configure mocks for order creation
        _server.PaymentService.ProcessShouldSucceed = true;
        _server.PaymentService.PaymentIdToReturn = "PaymentId";
        
        _server.InventoryService.ReserveShouldSucceed = true;
        _server.InventoryService.ReservationIdToReturn = "ReservationId";

        _server.ShipmentServer
            .Given(Request.Create().WithPath("/api/shipping/create").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new { shipmentId = "ShipmentId", trackingNumber = "TrackingNumber" }));

        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var orderRequest = new CreateOrderRequest
        {
            CustomerId = customerId.ToString(),
            PaymentMethod = "CreditCard",
            IdempotencyKey = $"order-for-return-{Guid.NewGuid()}",
            DeliveryAddress = new Address
            {
                Street = "123 Main St",
                City = "New York",
                Country = "USA",
                PostalCode = "10001"
            }
        };

        orderRequest.Items.Add(new OrderItem
        {
            ProductId = productId.ToString(),
            Quantity = 1,
            Price = 29.99m.ToDecimalValue(),
            Currency = "USD"
        });

        var orderResponse = await _client.CreateOrderAsync(orderRequest);
        var orderId = Guid.Parse(orderResponse.OrderId);

        // Wait for order saga to complete
        var saga = await _server.WaitForSagaStatusAsync(
            orderId, SagaTypes.OrderSaga, SagaStatus.Completed, timeoutSeconds: 30);
        saga.Should().NotBeNull("order saga must complete before requesting return");

        return (orderId, productId);
    }


    // publishes event to Kafka to simulate webhook, triggers continuation to resume the paused ReturnSaga
    private async Task PublishReturnShipmentDeliveredEventToKafkaAsync(
        Guid orderId,
        string shipmentId,
        string trackingNumber,
        DateTime deliveredAt)
    {
        var eventWrapper = new
        {
            EventType = "ReturnShipmentDeliveredEvent",
            EventId = Guid.NewGuid(),
            OccurredOn = DateTime.UtcNow,
            Payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                OrderId = orderId,
                ShipmentId = shipmentId,
                TrackingNumber = trackingNumber,
                DeliveredAt = deliveredAt
            })
        };

        var config = new ProducerConfig
        {
            BootstrapServers = _server.KafkaBootstrapServers
        };

        using var producer = new ProducerBuilder<string, string>(config).Build();
        
        await producer.ProduceAsync(
            "order.events",
            new Message<string, string>
            {
                Key = orderId.ToString(),
                Value = System.Text.Json.JsonSerializer.Serialize(eventWrapper)
            });

        _output.WriteLine($"📤 Published ReturnShipmentDeliveredEvent to Kafka for order {orderId}");
    }

    private async Task<SagaState?> WaitForSagaStatusAsync(
        Guid correlationId,
        string sagaType,
        Func<SagaStatus, bool> statusPredicate,
        int timeoutSeconds = 90)
    {
        for (var i = 0; i < timeoutSeconds; i++)
        {
            var state = await _server.GetSagaStateAsync(correlationId, sagaType);
            if (state != null && statusPredicate(state.Status))
                return state;

            await Task.Delay(1000);
        }

        return await _server.GetSagaStateAsync(correlationId, sagaType);
    }
}