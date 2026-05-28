using Payments.Domain.Transactions;

namespace Payments.Application.Common.Data;

/// <summary>
/// Application-layer port over the <see cref="PaymentTransaction"/> persistence root. Concrete
/// implementation lives in <c>Payments.Infrastructure</c> (it wraps
/// <c>PaymentsDbContext.Transactions</c>). This interface exists primarily for unit-test
/// ergonomics: mocking <see cref="System.Linq.IQueryable"/> over a <see cref="Microsoft.EntityFrameworkCore.DbSet{TEntity}"/>
/// is awkward, so handlers depend on this port and the adapter is the only place EF Core LINQ
/// runs against the aggregate set.
/// </summary>
public interface IPaymentRepository
{
    /// <summary>
    /// Returns the aggregate with the given <paramref name="paymentId"/> for mutation, or
    /// <c>null</c> if no such row exists. Tracking is enabled — the caller mutates the aggregate
    /// and the eventual <c>SaveChangesAsync</c> on the outbox / DbContext flushes the changes.
    /// Use this from the four command handlers (Authorize / Capture / Void / RequestRefund);
    /// the read-side <c>GetPaymentByIdQueryHandler</c> must use
    /// <see cref="GetByIdAsNoTrackingAsync"/> instead.
    /// </summary>
    Task<PaymentTransaction?> GetByIdForUpdateAsync(Guid paymentId, CancellationToken ct);

    /// <summary>
    /// Returns the aggregate with the given <paramref name="paymentId"/> as a detached read-only
    /// projection, or <c>null</c> if no such row exists (#251). Tracking is disabled — the change
    /// tracker carries no overhead for the request and a future mapper that accidentally mutates
    /// the entity cannot leak that mutation into a downstream <c>SaveChangesAsync</c>. Use this
    /// from <c>GetPaymentByIdQueryHandler</c>; command handlers must use
    /// <see cref="GetByIdForUpdateAsync"/> instead.
    /// </summary>
    Task<PaymentTransaction?> GetByIdAsNoTrackingAsync(Guid paymentId, CancellationToken ct);

    /// <summary>
    /// Returns the aggregate whose <c>CorrelationId</c> matches <paramref name="correlationId"/>
    /// for mutation, or <c>null</c> if no such row exists. The unique index on
    /// <c>payment_transactions.correlation_id</c> guarantees at-most-one row. Used by the
    /// Capture / Void / RequestRefund command handlers post-#255: the saga identifies the
    /// aggregate by CorrelationId on those wire commands (PaymentTransactionId is only present
    /// on AuthorizePaymentCommand + RequestRefundCommand, which suffices since the saga has
    /// PaymentTransactionId in its own state but not all command DTOs carry it). Tracking is
    /// enabled — caller mutates and the outbox-shared SaveChangesAsync flushes.
    /// </summary>
    Task<PaymentTransaction?> GetByCorrelationIdForUpdateAsync(Guid correlationId, CancellationToken ct);

    /// <summary>
    /// Returns all payment transactions for a given order, in deterministic order. Read-only
    /// — used by the admin <c>GET /api/v1/payments?orderId=…</c> endpoint; the
    /// implementation uses <c>AsNoTracking</c>.
    /// </summary>
    Task<IReadOnlyList<PaymentTransaction>> GetByOrderIdAsync(Guid orderId, CancellationToken ct);

    /// <summary>
    /// Adds a freshly-created aggregate to the persistence context. Synchronous because EF
    /// Core's <c>DbSet.Add</c> is synchronous; the actual SQL <c>INSERT</c> happens at
    /// <c>SaveChangesAsync</c> time on the shared outbox transaction.
    /// </summary>
    void Add(PaymentTransaction paymentTransaction);
}
