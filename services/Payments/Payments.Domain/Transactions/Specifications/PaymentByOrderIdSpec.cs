using Ardalis.Specification;

namespace Payments.Domain.Transactions.Specifications;

/// <summary>
/// Finds the <see cref="PaymentTransaction"/> aggregate for a given <c>OrderId</c> — the saga
/// business key per <see href="../../../../docs/adr/0029-order-keyed-saga-and-pre-assigned-orderid.md">ADR-0029</see>.
/// Used by the Capture / Void command handlers, which carry only the order-scoped saga key on the
/// wire (not the PaymentTransactionId). The unique index on <c>payment_transactions.order_id</c>
/// guarantees at-most-one match, so a hit is the definitive aggregate for the step.
/// </summary>
/// <remarks>
/// Tagged with the spec class name for SQL-level observability (EF Core emits the tag as a query
/// comment). Write-side only per ADR-0021.
/// </remarks>
public sealed class PaymentByOrderIdSpec : Specification<PaymentTransaction>, ISingleResultSpecification<PaymentTransaction>
{
    public PaymentByOrderIdSpec(Guid orderId)
    {
        Query
            .Where(t => t.OrderId == orderId)
            .TagWith(nameof(PaymentByOrderIdSpec));
    }
}
