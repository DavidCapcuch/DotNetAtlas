using Microsoft.EntityFrameworkCore.Storage;
using Platform.OutboxRelay.WorkerService.OutboxRelay;

namespace Platform.OutboxRelay.WorkerService.IntegrationTests;

/// <summary>
/// An outbox write whose ids are assigned but whose rows are not yet visible, held open until
/// <see cref="CommitAsync"/>. Disposing without committing rolls the write back.
/// </summary>
public sealed class PendingOutboxWrite : IAsyncDisposable
{
    private readonly OutboxDbContext _dbContext;
    private readonly IDbContextTransaction _transaction;

    internal PendingOutboxWrite(
        OutboxDbContext dbContext,
        IDbContextTransaction transaction,
        IReadOnlyList<long> ids)
    {
        _dbContext = dbContext;
        _transaction = transaction;
        Ids = ids;
    }

    /// <summary>Ids Postgres stamped at INSERT — readable here while no other session can see the rows.</summary>
    public IReadOnlyList<long> Ids { get; }

    public Task CommitAsync(CancellationToken ct) => _transaction.CommitAsync(ct);

    public async ValueTask DisposeAsync()
    {
        await _transaction.DisposeAsync();
        await _dbContext.DisposeAsync();
    }
}
