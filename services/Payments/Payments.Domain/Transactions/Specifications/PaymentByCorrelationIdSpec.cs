using Ardalis.Specification;

namespace Payments.Domain.Transactions.Specifications;

/// <summary>
/// Finds the <see cref="PaymentTransaction"/> aggregate for a given saga correlation id —
/// used by the Capture / Void / RequestRefund command handlers to resolve the aggregate for
/// mutation. Those saga steps carry only the <c>CorrelationId</c> on the wire (not the
/// PaymentTransactionId; see #255), and the unique index on
/// <c>payment_transactions.correlation_id</c> guarantees at-most-one match, so a hit is the
/// definitive aggregate for the step.
/// </summary>
/// <remarks>
/// Tagged with the spec class name for SQL-level observability (EF Core emits the tag as a
/// comment in the generated query) — invaluable when tracing saga idempotency / Kafka
/// redelivery in production logs. Write-side only per ADR-0021; the read side projects inline.
/// </remarks>
public sealed class PaymentByCorrelationIdSpec : Specification<PaymentTransaction>, ISingleResultSpecification<PaymentTransaction>
{
    public PaymentByCorrelationIdSpec(Guid correlationId)
    {
        Query
            .Where(t => t.CorrelationId == correlationId)
            .TagWith(nameof(PaymentByCorrelationIdSpec));
    }
}
