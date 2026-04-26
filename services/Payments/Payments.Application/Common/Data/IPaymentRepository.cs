using Payments.Domain.Transactions;

namespace Payments.Application.Common.Data;

/// <summary>
/// Application-layer port over the <see cref="PaymentTransaction"/> persistence root. Concrete
/// implementation lives in <c>Payments.Infrastructure</c> and lands in M5 (it wraps
/// <c>PaymentsDbContext.Transactions</c>). This interface exists primarily for unit-test
/// ergonomics: mocking <see cref="System.Linq.IQueryable"/> over a <see cref="Microsoft.EntityFrameworkCore.DbSet{TEntity}"/>
/// is awkward, so handlers depend on this port and the M5 adapter is the only place EF Core LINQ
/// runs against the aggregate set.
/// </summary>
public interface IPaymentRepository
{
    /// <summary>
    /// Returns the aggregate with the given <paramref name="paymentId"/>, or <c>null</c> if no
    /// such row exists. Tracking is enabled — the caller mutates the aggregate, and the eventual
    /// <c>SaveChangesAsync</c> on the outbox / DbContext flushes the changes.
    /// </summary>
    Task<PaymentTransaction?> GetByIdAsync(Guid paymentId, CancellationToken ct);

    /// <summary>
    /// Returns all payment transactions for a given order, in deterministic order. Read-only
    /// — used by the admin <c>GET /api/v1/payments?orderId=…</c> endpoint (M6) so its tracking
    /// behavior is irrelevant; the M5 implementation may use <c>AsNoTracking</c>.
    /// </summary>
    Task<IReadOnlyList<PaymentTransaction>> GetByOrderIdAsync(Guid orderId, CancellationToken ct);

    /// <summary>
    /// Adds a freshly-created aggregate to the persistence context. Synchronous because EF
    /// Core's <c>DbSet.Add</c> is synchronous; the actual SQL <c>INSERT</c> happens at
    /// <c>SaveChangesAsync</c> time on the shared outbox transaction.
    /// </summary>
    void Add(PaymentTransaction paymentTransaction);
}
