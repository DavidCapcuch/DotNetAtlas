using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Platform.ReliableMessaging.Inbox.Core;

namespace Platform.ReliableMessaging.Inbox.EFCore;

/// <summary>
/// Interface for DbContext that supports the Inbox pattern for idempotent message processing.
/// </summary>
public interface IInboxDbContext
{
    /// <summary>
    /// Gets the DbSet of inbox messages for tracking processed messages.
    /// </summary>
    DbSet<InboxMessage> InboxMessages { get; }

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
