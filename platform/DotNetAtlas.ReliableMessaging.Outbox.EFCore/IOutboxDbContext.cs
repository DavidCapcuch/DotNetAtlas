using DotNetAtlas.ReliableMessaging.Outbox.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DotNetAtlas.ReliableMessaging.Outbox.EFCore;

/// <summary>
/// Contract for DbContext that supports the outbox pattern.
/// </summary>
public interface IOutboxDbContext
{
    /// <summary>
    /// Gets the DbSet of outbox messages for reliable event publishing.
    /// </summary>
    DbSet<OutboxMessage> OutboxMessages { get; }

    /// <summary>
    /// Gets the database facade for the context, providing access to database-related operations
    /// such as transactions and execution strategies.
    /// </summary>
    DatabaseFacade Database { get; }

    /// <summary>
    /// Saves all changes made in this context to the database.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>The number of state entries written to the database.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
