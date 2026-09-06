using Domain.Enums;
using Domain.Exceptions;
using Domain.ValueObjects;

namespace Domain.Entities;


public sealed class LedgerTransaction
{
    private readonly List<LedgerEntry> _entries = [];
    public Guid Id { get; private set; }
    public string TransactionRef { get; private set; } = null!;
    public Guid? OrderId { get; private set; }
    public Guid? PaymentId { get; private set; }
    public TransactionRefType RefType { get; private set; }
    public string RefId { get; private set; } = null!;
    public string Currency { get; private set; } = null!;
    public DateTime OccurredAt { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Set only for operator-posted adjustments
    public string? Reason { get; private set; }
    public string? PostedBy { get; private set; }

    public IReadOnlyCollection<LedgerEntry> Entries => _entries.AsReadOnly();

    private LedgerTransaction()
    {
    }

    private LedgerTransaction(
        string transactionRef,
        TransactionRefType refType,
        string refId,
        string currency,
        Guid? orderId,
        Guid? paymentId,
        DateTime occurredAt)
    {
        Id = Guid.NewGuid();
        TransactionRef = transactionRef;
        RefType = refType;
        RefId = refId;
        Currency = currency;
        OrderId = orderId;
        PaymentId = paymentId;
        OccurredAt = occurredAt;
        CreatedAt = DateTime.UtcNow;
    }

 public static LedgerTransaction ForAuthorization(
        Guid? orderId,
        Guid? paymentId,
        string paymentRef,
        Money amount,
        DateTime occurredAt)
    {
        var tx = NewPaymentTransaction(
            transactionRef: $"authorize:{RequirePaymentRef(paymentRef)}",
            refType: TransactionRefType.Authorize,
            paymentRef: paymentRef,
            currency: amount.Currency,
            orderId: orderId,
            paymentId: paymentId,
            occurredAt: occurredAt);

        tx.AddEntry(LedgerAccount.CustomerAuthorized, EntryDirection.Debit, amount);
        tx.AddEntry(LedgerAccount.AuthorizationHold, EntryDirection.Credit, amount);
        tx.EnsureBalanced();
        return tx;
    }

    // Releases a hold without taking the money: the exact mirror of ForAuthorization
    public static LedgerTransaction ForAuthorizationVoid(
        Guid? orderId,
        Guid? paymentId,
        string paymentRef,
        Money amount,
        DateTime occurredAt)
    {
        var tx = NewPaymentTransaction(
            transactionRef: $"void:{RequirePaymentRef(paymentRef)}",
            refType: TransactionRefType.Void,
            paymentRef: paymentRef,
            currency: amount.Currency,
            orderId: orderId,
            paymentId: paymentId,
            occurredAt: occurredAt);

        tx.AddEntry(LedgerAccount.AuthorizationHold, EntryDirection.Debit, amount);
        tx.AddEntry(LedgerAccount.CustomerAuthorized, EntryDirection.Credit, amount);
        tx.EnsureBalanced();
        return tx;
    }

   public static LedgerTransaction ForCapture(
        Guid? orderId,
        Guid? paymentId,
        string paymentRef,
        Money gross,
        decimal fee,
        decimal tax,
        DateTime occurredAt)
    {
        if (fee < 0m)
            throw new ArgumentOutOfRangeException(nameof(fee), "Gateway fee cannot be negative.");

        if (tax < 0m)
            throw new ArgumentOutOfRangeException(nameof(tax), "Tax cannot be negative.");

        if (fee > gross.Amount)
            throw new ArgumentOutOfRangeException(
                nameof(fee), $"Gateway fee {fee} exceeds the captured amount {gross.Amount}.");

        if (tax > gross.Amount)
            throw new ArgumentOutOfRangeException(
                nameof(tax), $"Tax {tax} exceeds the captured amount {gross.Amount}.");

        var tx = NewPaymentTransaction(
            transactionRef: $"capture:{RequirePaymentRef(paymentRef)}",
            refType: TransactionRefType.Capture,
            paymentRef: paymentRef,
            currency: gross.Currency,
            orderId: orderId,
            paymentId: paymentId,
            occurredAt: occurredAt);

        tx.AddEntryIfPositive(LedgerAccount.CustomerCaptured, EntryDirection.Debit, gross.Amount - fee, gross.Currency);
        tx.AddEntryIfPositive(LedgerAccount.GatewayFees, EntryDirection.Debit, fee, gross.Currency);
        tx.AddEntryIfPositive(LedgerAccount.MerchantRevenue, EntryDirection.Credit, gross.Amount - tax, gross.Currency);
        tx.AddEntryIfPositive(LedgerAccount.TaxPayable, EntryDirection.Credit, tax, gross.Currency);
        tx.EnsureBalanced();
        return tx;
    }

    // Pays out the refund liability in cash: Dr refunds_payable / Cr customer_captured
    // our dept actually paid
    public static LedgerTransaction ForRefund(
        Guid? orderId,
        string refundId,
        Money amount,
        DateTime occurredAt,
        Guid? paymentId = null)
    {
        if (string.IsNullOrWhiteSpace(refundId))
            throw new ArgumentException("RefundId is required.", nameof(refundId));

        var tx = new LedgerTransaction(
            transactionRef: $"refund:{refundId.Trim()}",
            refType: TransactionRefType.Refund,
            refId: refundId.Trim(),
            currency: amount.Currency,
            orderId: orderId,
            paymentId: paymentId,
            occurredAt: occurredAt);

        tx.AddEntry(LedgerAccount.RefundsPayable, EntryDirection.Debit, amount);
        tx.AddEntry(LedgerAccount.CustomerCaptured, EntryDirection.Credit, amount);
        tx.EnsureBalanced();
        return tx;
    }

    // Takes back previously recognized revenue on a return: Dr merchant_revenue / Cr refunds_payable
    // we now owe this amount back to the customer
    public static LedgerTransaction ForRevenueReversal(
        Guid orderId,
        Guid returnRequestId,
        Money amount,
        DateTime occurredAt)
    {
        // An order can be returned more than once, so only the return request identifies a reversal
        if (returnRequestId == Guid.Empty)
            throw new ArgumentException("ReturnRequestId is required.", nameof(returnRequestId));

        var tx = new LedgerTransaction(
            transactionRef: $"reversal:{returnRequestId}",
            refType: TransactionRefType.Reversal,
            refId: returnRequestId.ToString(),
            currency: amount.Currency,
            orderId: orderId,
            paymentId: null,
            occurredAt: occurredAt);

        tx.AddEntry(LedgerAccount.MerchantRevenue, EntryDirection.Debit, amount);
        tx.AddEntry(LedgerAccount.RefundsPayable, EntryDirection.Credit, amount);
        tx.EnsureBalanced();
        return tx;
    }

    //append-only reversing transaction that cancels a prior revenue reversal by swapping every leg.
    public static LedgerTransaction ForReversalCancellation(LedgerTransaction original, DateTime occurredAt)
    {
        if (original.RefType != TransactionRefType.Reversal)
            throw new InvalidOperationException(
                $"Transaction {original.Id} is not a revenue reversal and cannot be cancelled.");

        var tx = new LedgerTransaction(
            transactionRef: $"cancel-reversal:{original.Id}",
            refType: TransactionRefType.ReversalCancellation,
            refId: original.Id.ToString(),
            currency: original.Currency,
            orderId: original.OrderId,
            paymentId: original.PaymentId,
            occurredAt: occurredAt);

        foreach (var entry in original._entries)
        {
            var reversed = entry.Direction == EntryDirection.Debit
                ? EntryDirection.Credit
                : EntryDirection.Debit;

            tx.AddEntry(entry.Account, reversed, new Money(entry.Amount, entry.Currency));
        }

        tx.EnsureBalanced();
        return tx;
    }

    public static LedgerTransaction ForManualAdjustment(
        string adjustmentId,
        string currency,
        IReadOnlyList<AdjustmentLeg> legs,
        string reason,
        string postedBy,
        Guid? orderId,
        DateTime occurredAt)
    {
        if (string.IsNullOrWhiteSpace(adjustmentId))
            throw new ArgumentException("AdjustmentId is required.", nameof(adjustmentId));

        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required.", nameof(currency));

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required for a manual adjustment.", nameof(reason));

        if (string.IsNullOrWhiteSpace(postedBy))
            throw new ArgumentException("PostedBy is required for a manual adjustment.", nameof(postedBy));

        if (legs is null || legs.Count < 2)
            throw new ArgumentException(
                "A manual adjustment needs at least two legs to be balanced.", nameof(legs));

        var normalizedCurrency = currency.Trim().ToUpperInvariant();

        var tx = new LedgerTransaction(
            transactionRef: $"adjustment:{adjustmentId.Trim()}",
            refType: TransactionRefType.Adjustment,
            refId: adjustmentId.Trim(),
            currency: normalizedCurrency,
            orderId: orderId,
            paymentId: null,
            occurredAt: occurredAt)
        {
            Reason = reason.Trim(),
            PostedBy = postedBy.Trim(),
        };

        foreach (var leg in legs)
            tx.AddEntry(leg.Account, leg.Direction, new Money(leg.Amount, normalizedCurrency));

        tx.EnsureBalanced();
        return tx;
    }

    private static string RequirePaymentRef(string paymentRef)
    {
        if (string.IsNullOrWhiteSpace(paymentRef))
            throw new ArgumentException("PaymentRef is required.", nameof(paymentRef));

        return paymentRef.Trim();
    }

    private static LedgerTransaction NewPaymentTransaction(
        string transactionRef,
        TransactionRefType refType,
        string paymentRef,
        string currency,
        Guid? orderId,
        Guid? paymentId,
        DateTime occurredAt) =>
        new(transactionRef, refType, paymentRef.Trim(), currency, orderId, paymentId, occurredAt);

    private void AddEntry(LedgerAccount account, EntryDirection direction, Money amount)
    {
        _entries.Add(new LedgerEntry(Id, account, direction, amount.Amount, amount.Currency, CreatedAt));
    }

    private void AddEntryIfPositive(LedgerAccount account, EntryDirection direction, decimal amount, string currency)
    {
        if (amount <= 0m)
            return;

        _entries.Add(new LedgerEntry(Id, account, direction, amount, currency, CreatedAt));
    }

    private void EnsureBalanced()
    {
        if (_entries.Count == 0)
            throw new UnbalancedTransactionException($"Transaction {TransactionRef} has no entries.");

        foreach (var group in _entries.GroupBy(e => e.Currency))
        {
            var debits = group.Where(e => e.Direction == EntryDirection.Debit).Sum(e => e.Amount);
            var credits = group.Where(e => e.Direction == EntryDirection.Credit).Sum(e => e.Amount);

            if (debits != credits)
                throw new UnbalancedTransactionException(
                    $"Transaction {TransactionRef} is unbalanced for {group.Key}: debits={debits}, credits={credits}.");
        }
    }
}
