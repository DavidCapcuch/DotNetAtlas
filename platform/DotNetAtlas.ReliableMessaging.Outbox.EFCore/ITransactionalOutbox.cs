using Avro.Specific;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DotNetAtlas.ReliableMessaging.Outbox.EFCore;

/// <summary>
/// Scoped interface for transactional outbox operations with a specific DbContext type.
/// Use this interface in request-scoped handlers where the DbContext is injected via DI.
/// </summary>
/// <typeparam name="TContext">The DbContext type that implements <see cref="IOutboxDbContext"/>.</typeparam>
/// <remarks>
/// This interface is registered as Scoped to match the DbContext lifetime.
/// Both the transactional outbox and the DbContext share the same instance within a scope,
/// ensuring all changes are saved in the same transaction.
/// <para>
/// For <see cref="Microsoft.EntityFrameworkCore.IDbContextFactory{TContext}"/> scenarios,
/// use <see cref="IOutboxWriter"/> instead and pass the DbContext explicitly.
/// </para>
/// <para>
/// This interface exposes <see cref="Database"/> and <see cref="SaveChangesAsync"/> for convenience,
/// enabling handlers that only need transactional outbox operations to use a single injection.
/// See the design decision documentation for rationale.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public class KafkaMessageHandler
/// {
///     private readonly ITransactionalOutbox&lt;IWeatherDbContext&gt; _outbox;
///
///     public async Task Handle(SomeEvent message, CancellationToken ct)
///     {
///         await _outbox.Database.EnsureTransactionAsync(async () =>
///         {
///             // Process message...
///             _outbox.AddOutboxMessage("weather.events", key, integrationEvent);
///             await _outbox.SaveChangesAsync(ct);
///         }, ct);
///     }
/// }
/// </code>
/// </example>
public interface ITransactionalOutbox<TContext>
    where TContext : IOutboxDbContext
{
    /// <summary>
    /// Adds an integration event to the outbox table for reliable publishing.
    /// The message will be persisted in the same transaction as other DbContext changes.
    /// </summary>
    /// <param name="topicName">The Kafka topic where this message will be published.</param>
    /// <param name="kafkaKey">The Kafka key for message partitioning (typically aggregate ID).</param>
    /// <param name="integrationEvent">The Avro integration event to publish.</param>
    void AddOutboxMessage(string topicName, string? kafkaKey, ISpecificRecord integrationEvent);

    /// <summary>
    /// Saves all changes made in the underlying DbContext to the database.
    /// This is a convenience method that delegates to the DbContext's SaveChangesAsync.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>The number of state entries written to the database.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the database facade for transaction management.
    /// Use with <see cref="Common.DatabaseFacadeExtensions.EnsureTransactionAsync"/> for transactional outbox operations.
    /// </summary>
    /// <remarks>
    /// Exposed for convenience to support the common pattern of wrapping outbox operations in a transaction.
    /// This is a pragmatic design choice - see design decision documentation for rationale.
    /// </remarks>
    DatabaseFacade Database { get; }
}
