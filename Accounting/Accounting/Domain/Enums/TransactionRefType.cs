namespace Domain.Enums;

public enum TransactionRefType
{
    Authorize = 0,             // a card hold was placed
    Void = 1,                  // a hold was released without capturing
    Capture = 2,               // money was actually taken from the customer
    Refund = 3,                // money was paid back to the customer
    Reversal = 4,              // revenue was reversed (e.g. due to a return)
    ReversalCancellation = 5,  // a prior revenue reversal was undone
    Chargeback = 6,            // a card network dispute reversed a payment
    Adjustment = 7             // an operator posted a manual correcting entry
}
