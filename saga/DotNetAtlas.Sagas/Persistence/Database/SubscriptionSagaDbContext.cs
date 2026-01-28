using DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga;
using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga;
using DotNetAtlas.Sagas.WeatherAlerts.PurchaseAlertSubscriptionSaga;
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

    public DbSet<SubscriptionPurchaseSagaState> SubscriptionPurchaseSagaStates { get; set; } = null!;
    public DbSet<SubscriptionExtensionSagaState> SubscriptionExtensionSagaStates { get; set; } = null!;
    public DbSet<PaymentSagaState> PaymentSagaStates { get; set; } = null!;

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
