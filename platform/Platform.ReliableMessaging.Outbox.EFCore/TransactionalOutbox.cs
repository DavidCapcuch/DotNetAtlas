using Avro.Specific;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Platform.ReliableMessaging.Outbox.EFCore;

/// <summary>
/// Scoped implementation of <see cref="ITransactionalOutbox{TContext}"/> that uses
/// the injected DbContext instance.
/// </summary>
/// <typeparam name="TContext">The DbContext type that implements <see cref="IOutboxDbContext"/>.</typeparam>
/// <remarks>
/// This class is registered as Scoped to match the DbContext lifetime.
/// It delegates serialization to the singleton <see cref="IOutboxWriter"/> implementation.
/// </remarks>
public class TransactionalOutbox<TContext> : ITransactionalOutbox<TContext>
    where TContext : class, IOutboxDbContext
{
    private readonly TContext _dbContext;
    private readonly IOutboxWriter _coreWriter;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransactionalOutbox{TContext}"/> class.
    /// </summary>
    /// <param name="dbContext">The scoped DbContext instance.</param>
    /// <param name="coreWriter">The core outbox writer for serialization.</param>
    public TransactionalOutbox(TContext dbContext, IOutboxWriter coreWriter)
    {
        _dbContext = dbContext;
        _coreWriter = coreWriter;
    }

    /// <inheritdoc />
    public void AddOutboxMessage(string topicName, string? kafkaKey, ISpecificRecord integrationEvent)
    {
        _coreWriter.AddOutboxMessage(_dbContext, topicName, kafkaKey, integrationEvent);
    }

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public DatabaseFacade Database => _dbContext.Database;
}
