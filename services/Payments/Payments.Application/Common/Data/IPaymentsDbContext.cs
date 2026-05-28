using Microsoft.EntityFrameworkCore;
using Payments.Domain.Transactions;
using Platform.ReliableMessaging.Outbox.EFCore;

namespace Payments.Application.Common.Data;

/// <summary>
/// Application-layer port for the Payments persistence context. Concrete <c>PaymentsDbContext</c>
/// lives in <c>Payments.Infrastructure</c>; handlers depend on this interface so they can be
/// unit-tested without EF Core. Extends <see cref="IOutboxDbContext"/> so transactional-outbox
/// writes share the same <c>SaveChangesAsync</c> call as the aggregate write — atomicity is
/// preserved by EF's implicit single-<c>SaveChanges</c> transaction.
/// </summary>
public interface IPaymentsDbContext : IOutboxDbContext
{
    /// <summary>
    /// Write-model set for the <see cref="PaymentTransaction"/> aggregate. Configured with
    /// snake_case naming, <c>*_enc</c> columns for sensitive tokens (ADR-0011), and
    /// <c>timestamptz</c> mappings for all <c>DateTimeOffset</c> properties (ADR-0015).
    /// </summary>
    DbSet<PaymentTransaction> Transactions { get; }
}
