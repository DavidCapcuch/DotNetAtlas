using Avro.Specific;

namespace Platform.ReliableMessaging.Outbox.EFCore;

/// <summary>
/// Core interface for adding messages to the outbox.
/// Use this interface when working with <see cref="Microsoft.EntityFrameworkCore.IDbContextFactory{TContext}"/>
/// or when you need to pass the DbContext explicitly.
/// </summary>
/// <remarks>
/// This interface is registered as a Singleton since it only depends on stateless services.
/// For scoped DbContext scenarios, prefer <see cref="IOutboxWriter"/> which
/// automatically uses the injected DbContext instance.
/// </remarks>
/// <example>
/// <code>
/// public class BackgroundService
/// {
///     private readonly IDbContextFactory&lt;CatalogDbContext&gt; _factory;
///     private readonly IOutboxWriter _outboxWriter;
///
///     public async Task DoWork()
///     {
///         await using var dbContext = await _factory.CreateDbContextAsync();
///         _outboxWriter.AddOutboxMessage(dbContext, "catalog.products", key, integrationEvent);
///         await dbContext.SaveChangesAsync();
///     }
/// }
/// </code>
/// </example>
public interface IOutboxWriter
{
    /// <summary>
    /// Adds an integration event to the outbox table for reliable publishing.
    /// The message will be persisted in the same transaction as other DbContext changes.
    /// </summary>
    /// <param name="dbContext">The DbContext instance implementing <see cref="IOutboxDbContext"/>.</param>
    /// <param name="topicName">The Kafka topic where this message will be published.</param>
    /// <param name="kafkaKey">The Kafka key for message partitioning (typically aggregate ID).</param>
    /// <param name="integrationEvent">The Avro integration event to publish.</param>
    void AddOutboxMessage(IOutboxDbContext dbContext, string topicName, string? kafkaKey, ISpecificRecord integrationEvent);
}
