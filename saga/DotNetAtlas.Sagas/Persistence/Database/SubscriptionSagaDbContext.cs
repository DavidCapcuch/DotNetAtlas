using DotNetAtlas.Sagas.Finance.PaymentSaga;
using DotNetAtlas.Sagas.Orders.ExtendAlertSubscriptionSaga;
using DotNetAtlas.Sagas.Orders.PurchaseAlertSubscriptionSaga;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;

namespace DotNetAtlas.Sagas.Persistence.Database;

public class SubscriptionSagaDbContext : SagaDbContext
{
    public const string DefaultSchemaName = "saga";

    public SubscriptionSagaDbContext(DbContextOptions<SubscriptionSagaDbContext> options)
        : base(options)
    {
    }

    public DbSet<SubscriptionPurchaseSagaState> SubscriptionPurchaseSagaStates { get; set; }
    public DbSet<SubscriptionExtensionSagaState> SubscriptionExtensionSagaStates { get; set; }
    public DbSet<PaymentSagaState> PaymentSagaStates { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(DefaultSchemaName);
    }

    protected override IEnumerable<ISagaClassMap> Configurations
    {
        get
        {
            yield return new SubscriptionPurchaseSagaStateMap();
            yield return new SubscriptionExtensionSagaStateMap();
            yield return new PaymentSagaStateMap();
        }
    }
}
