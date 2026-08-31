using Application.Commands.IngestMoneyEvent;
using Application.Common;
using Application.Contracts;
using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Application.Tests;

public class IngestMoneyEventCommandHandlerTests
{
    private static readonly DateTime OccurredAt = new(2026, 8, 30, 9, 0, 0, DateTimeKind.Utc);

    private readonly FakeLedgerTransactionRepository _ledger = new();
    private readonly FakeProcessedEventRepository _processedEvents = new();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IngestMoneyEventCommandHandler _handler;

    public IngestMoneyEventCommandHandlerTests()
    {
        _handler = new IngestMoneyEventCommandHandler(
            _ledger,
            _processedEvents,
            _unitOfWork,
            NullLogger<IngestMoneyEventCommandHandler>.Instance);
    }

    private static MoneyEventPayload Payload(
        string eventType = MoneyEventTypes.PaymentCaptured,
        string eventId = "pay-1:captured",
        string? refundId = null,
        decimal amount = 100m,
        decimal fee = 0m,
        decimal tax = 0m) =>
        new(
            EventId: eventId,
            EventType: eventType,
            PaymentId: "pay-1",
            OrderId: Guid.NewGuid().ToString(),
            RefundId: refundId,
            ProviderPaymentIntentId: "pi_1",
            Amount: amount,
            Currency: "EUR",
            Fee: fee,
            Tax: tax,
            OccurredAt: OccurredAt);

    [Fact]
    public async Task Handle_ShouldPostOneTransactionAndMarkTheEventProcessed()
    {
        var result = await _handler.Handle(new IngestMoneyEventCommand(Payload()), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var posted = Assert.Single(_ledger.Transactions);
        Assert.Equal("capture:pay-1", posted.TransactionRef);
        Assert.Equal(TransactionRefType.Capture, posted.RefType);

        Assert.Single(_processedEvents.Events);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldPostExactlyOneTransaction_WhenTheSameEventIsReplayed()
    {
        // Kafka delivery is at-least-once, so a redelivered event must not double-book.
        var command = new IngestMoneyEventCommand(Payload());

        var first = await _handler.Handle(command, CancellationToken.None);
        var second = await _handler.Handle(command, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal("duplicate", second.Value);

        Assert.Single(_ledger.Transactions);
        Assert.Single(_processedEvents.Events);
    }

    [Theory]
    [InlineData(MoneyEventTypes.PaymentAuthorized, "authorize:pay-1", TransactionRefType.Authorize)]
    [InlineData(MoneyEventTypes.PaymentVoided, "void:pay-1", TransactionRefType.Void)]
    [InlineData(MoneyEventTypes.PaymentCaptured, "capture:pay-1", TransactionRefType.Capture)]
    public async Task Handle_ShouldMapEachPaymentLegToItsOwnPosting(
        string eventType,
        string expectedRef,
        TransactionRefType expectedType)
    {
        var payload = Payload(eventType, eventId: $"pay-1:{eventType}");

        var result = await _handler.Handle(new IngestMoneyEventCommand(payload), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var posted = Assert.Single(_ledger.Transactions);
        Assert.Equal(expectedRef, posted.TransactionRef);
        Assert.Equal(expectedType, posted.RefType);
    }

    [Fact]
    public async Task Handle_ShouldKeyRefundOnTheRefundId_SoTheGrpcPathConverges()
    {
        var payload = Payload(MoneyEventTypes.RefundIssued, eventId: "ref-7:refunded", refundId: "ref-7");

        var result = await _handler.Handle(new IngestMoneyEventCommand(payload), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("refund:ref-7", Assert.Single(_ledger.Transactions).TransactionRef);
    }

    [Fact]
    public async Task Handle_ShouldNotPostASecondTime_WhenTheGrpcPathAlreadyRecordedTheRefund()
    {
        // Order's UpdateAccountingRecordsStep and this event derive the same ref from the same
        // Payment refund id, so whichever arrives second must only mark the event processed.
        _ledger.Seed(LedgerTransaction.ForRefund(Guid.NewGuid(), "ref-7", new Domain.ValueObjects.Money(100m, "EUR"), OccurredAt));

        var payload = Payload(MoneyEventTypes.RefundIssued, eventId: "ref-7:refunded", refundId: "ref-7");
        var result = await _handler.Handle(new IngestMoneyEventCommand(payload), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(_ledger.Transactions);
        Assert.Single(_processedEvents.Events);
    }

    [Fact]
    public async Task Handle_ShouldRejectUnknownEventType_WithoutPosting()
    {
        var payload = Payload("SomethingElseEvent");

        var result = await _handler.Handle(new IngestMoneyEventCommand(payload), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(_ledger.Transactions);
        Assert.Empty(_processedEvents.Events);
    }

    [Fact]
    public async Task Handle_ShouldRejectRefundWithoutRefundId()
    {
        var payload = Payload(MoneyEventTypes.RefundIssued, refundId: null);

        var result = await _handler.Handle(new IngestMoneyEventCommand(payload), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(_ledger.Transactions);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Handle_ShouldRejectNonPositiveAmount(decimal amount)
    {
        var result = await _handler.Handle(
            new IngestMoneyEventCommand(Payload(amount: amount)),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(_ledger.Transactions);
    }

    private sealed class FakeLedgerTransactionRepository : ILedgerTransactionRepository
    {
        public List<LedgerTransaction> Transactions { get; } = [];

        public void Seed(LedgerTransaction transaction) => Transactions.Add(transaction);

        public Task<LedgerTransaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Transactions.FirstOrDefault(t => t.Id == id));

        public Task<LedgerTransaction?> GetByTransactionRefAsync(
            string transactionRef,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Transactions.FirstOrDefault(t => t.TransactionRef == transactionRef));

        public Task AddAsync(LedgerTransaction transaction, CancellationToken cancellationToken = default)
        {
            Transactions.Add(transaction);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProcessedEventRepository : IProcessedEventRepository
    {
        public List<ProcessedEvent> Events { get; } = [];

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Events.Any(e => e.EventId == eventId));

        public Task AddAsync(ProcessedEvent processedEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(processedEvent);
            return Task.CompletedTask;
        }
    }
}
