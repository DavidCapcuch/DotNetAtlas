using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore.Common;
using SagaOrchestrators.Payments.PaymentProcessingSaga;

namespace SagaOrchestrators.Common.Persistence.Database;

public class SagaDbContext : MassTransit.EntityFrameworkCoreIntegration.SagaDbContext, IOutboxDbContext
{
    public const string DefaultSchemaName = "saga";

    public SagaDbContext(DbContextOptions<SagaDbContext> options)
        : base(options)
    {
    }

    public DbSet<PaymentProcessingSagaState> PaymentProcessingSagaStates { get; set; }
    public DbSet<Platform.ReliableMessaging.Outbox.Core.OutboxMessage> OutboxMessages { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(DefaultSchemaName);
        modelBuilder.ConfigureOutbox(schemaName: DefaultSchemaName, tableName: "OutboxMessages");
    }

    protected override IEnumerable<ISagaClassMap> Configurations
    {
        get
        {
            yield return new PaymentProcessingSagaStateMap();
        }
    }
}
