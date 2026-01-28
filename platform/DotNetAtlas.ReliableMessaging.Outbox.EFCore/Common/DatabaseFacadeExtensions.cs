using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DotNetAtlas.ReliableMessaging.Outbox.EFCore.Common;

/// <summary>
/// Extension methods for <see cref="DatabaseFacade"/> to ensure transactional execution.
/// </summary>
public static class DatabaseFacadeExtensions
{
    /// <summary>
    /// Executes the operation within a transaction. If a transaction already exists participates in it.
    /// Otherwise, creates a new transaction with the database's execution strategy.
    /// </summary>
    /// <param name="database">The database facade.</param>
    /// <param name="operation">The async operation to execute within the transaction.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task EnsureTransactionAsync(
        this DatabaseFacade database,
        Func<Task> operation,
        CancellationToken ct = default)
    {
        if (database.CurrentTransaction is not null)
        {
            await operation();
            return;
        }

        var executionStrategy = database.CreateExecutionStrategy();
        await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await database.BeginTransactionAsync(ct);
            await operation();
            await transaction.CommitAsync(ct);
        });
    }
}
