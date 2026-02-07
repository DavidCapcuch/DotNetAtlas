using DotNetAtlas.ReliableMessaging.Outbox.EFCore;
using DotNetAtlas.ReliableMessaging.Outbox.EFCore.Common;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;

namespace DotNetAtlas.Sagas.Persistence.Database;

public class SagaDbContext : MassTransit.EntityFrameworkCoreIntegration.SagaDbContext, IOutboxDbContext
{
    public const string DefaultSchemaName = "saga";

    public SagaDbContext(DbContextOptions<SagaDbContext> options)
        : base(options)
    {
    }

    public DbSet<AlertSubscriptionPurchaseSagaState> AlertSubscriptionPurchaseSagaStates { get; set; }
    public DbSet<AlertSubscriptionExtensionSagaState> AlertSubscriptionExtensionSagaStates { get; set; }
    public DbSet<PaymentProcessingSagaState> PaymentProcessingSagaStates { get; set; }
    public DbSet<DotNetAtlas.ReliableMessaging.Outbox.Core.OutboxMessage> OutboxMessages { get; set; }

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
            yield return new AlertSubscriptionPurchaseSagaStateMap();
            yield return new AlertSubscriptionExtensionSagaStateMap();
            yield return new PaymentProcessingSagaStateMap();
        }
    }
}
