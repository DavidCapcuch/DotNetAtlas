using Microsoft.EntityFrameworkCore;
using Ordering.Domain.Orders;
using Platform.ReliableMessaging.Outbox.EFCore;

namespace Ordering.Application.Common.Data;

/// <summary>
/// Application-layer port for the Ordering persistence context.
/// Implemented by <c>OrderingDbContext</c> in the Infrastructure layer.
/// Exposes only the DbSets required by Application handlers + the outbox
/// plumbing inherited from <see cref="IOutboxDbContext"/> so that
/// <c>ITransactionalOutbox&lt;IOrderingDbContext&gt;</c> can share the same
/// scope / transaction as the aggregate save.
/// </summary>
public interface IOrderingDbContext : IOutboxDbContext
{
    /// <summary>
    /// The <see cref="Order"/> aggregate set.
    /// </summary>
    DbSet<Order> Orders { get; }
}
