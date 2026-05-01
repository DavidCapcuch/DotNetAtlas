using Microsoft.EntityFrameworkCore;
using Payments.Application.Common.Data;
using Payments.Domain.Transactions;
using Platform.ReliableMessaging.Inbox.Core;
using Platform.ReliableMessaging.Inbox.EFCore;
using Platform.ReliableMessaging.Inbox.EFCore.Common;
using Platform.ReliableMessaging.Outbox.Core;
using Platform.ReliableMessaging.Outbox.EFCore.Common;
using Platform.SharedKernel.Base;
using SmartEnum.EFCore;

namespace Payments.Infrastructure.Persistence.Database;

/// <summary>
/// EF Core DbContext for the Payments bounded context. Implements the
/// <see cref="IPaymentsDbContext"/> application port and <see cref="IInboxDbContext"/> so the
/// 4 saga-command consumers can participate in the inbox-dedup + outbox-write transaction
/// (ADR-0008 correlation-id + reliable messaging).
/// </summary>
public sealed class PaymentsDbContext : DbContext, IPaymentsDbContext, IInboxDbContext
{
    /// <summary>Default Postgres schema for all Payments tables.</summary>
    public const string DefaultSchemaName = "payments";

    public PaymentsDbContext(DbContextOptions<PaymentsDbContext> options)
        : base(options)
    {
    }

    /// <inheritdoc />
    public DbSet<PaymentTransaction> Transactions => AggregateRootSet<PaymentTransaction>();

    /// <inheritdoc />
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    /// <inheritdoc />
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly)
            .HasDefaultSchema(DefaultSchemaName);

        modelBuilder.ConfigureOutbox(DefaultSchemaName);
        modelBuilder.ConfigureInbox(DefaultSchemaName);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ConfigureSmartEnum();
    }

    private DbSet<T> AggregateRootSet<T>()
        where T : class, IAggregateRoot => Set<T>();
}
