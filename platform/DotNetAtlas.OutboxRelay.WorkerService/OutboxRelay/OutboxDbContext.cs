using DotNetAtlas.Outbox.Core;
using DotNetAtlas.Outbox.EntityFrameworkCore.EntityConfiguration;
using DotNetAtlas.Outbox.EntityFrameworkCore.EntityFramework;
using DotNetAtlas.OutboxRelay.WorkerService.OutboxRelay.Config;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.OutboxRelay.WorkerService.OutboxRelay;

public sealed class OutboxDbContext : DbContext, IOutboxDbContext
{
    private readonly OutboxRelayOptions _outboxRelayOptions;

    public OutboxDbContext(
        DbContextOptions<OutboxDbContext> options,
        IOptions<OutboxRelayOptions> outboxRelayOptions)
        : base(options)
    {
        _outboxRelayOptions = outboxRelayOptions.Value;
    }

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(
            new OutboxMessageConfiguration(_outboxRelayOptions.SchemaName, _outboxRelayOptions.TableName));
    }
}
