using Confluent.SchemaRegistry;
using DotNetAtlas.Sagas.Common.Config;
using DotNetAtlas.Sagas.Common.Kafka;
using DotNetAtlas.Sagas.Persistence.Database;
using DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga;
using DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Observability;
using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga;
using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Observability;
using DotNetAtlas.Sagas.WeatherAlerts.PurchaseAlertSubscriptionSaga;
using DotNetAtlas.Sagas.WeatherAlerts.PurchaseAlertSubscriptionSaga.Observability;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;

namespace DotNetAtlas.Sagas.Common;

/// <summary>
/// Extension methods for registering saga services.
/// </summary>
public static class SagaDependencyInjection
{
    /// <summary>
    /// Adds saga orchestration services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSagaOrchestration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register configuration options with validation
        services.AddOptionsWithValidateOnStart<SagaOptions>()
            .BindConfiguration(SagaOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<ConnectionStringsOptions>()
            .BindConfiguration(ConnectionStringsOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<EfCoreOptions>()
            .BindConfiguration(EfCoreOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<SagaHealthCheckOptions>()
            .BindConfiguration(SagaHealthCheckOptions.Section)
            .ValidateDataAnnotations();

        var sagaOptions = configuration
            .GetRequiredSection(SagaOptions.Section)
            .Get<SagaOptions>()!;

        var efCoreOptions = configuration
            .GetRequiredSection(EfCoreOptions.Section)
            .Get<EfCoreOptions>()!;

        services.AddSagaDatabase(configuration, efCoreOptions);

        services.AddSingleton<ISchemaRegistryClient>(_ =>
            new CachedSchemaRegistryClient(new SchemaRegistryConfig
            {
                Url = sagaOptions.SchemaRegistryUrl
            }));

        services.AddMassTransit(cfg =>
        {
            cfg.SetKebabCaseEndpointNameFormatter();

            cfg.AddSagaStateMachine<SubscriptionPurchaseSaga, SubscriptionPurchaseSagaState>()
                .EntityFrameworkRepository(r =>
                {
                    r.ConcurrencyMode = ConcurrencyMode.Optimistic;
                    r.ExistingDbContext<SubscriptionSagaDbContext>();
                    r.LockStatementProvider = new SqlServerLockStatementProvider();
                })
                .Endpoint(e =>
                {
                    e.ConcurrentMessageLimit = sagaOptions.ConcurrencyLimit;
                    e.PrefetchCount = sagaOptions.ConcurrencyLimit * 2;
                });

            cfg.AddSagaStateMachine<SubscriptionExtensionSaga, SubscriptionExtensionSagaState>()
                .EntityFrameworkRepository(r =>
                {
                    r.ConcurrencyMode = ConcurrencyMode.Optimistic;
                    r.ExistingDbContext<SubscriptionSagaDbContext>();
                    r.LockStatementProvider = new SqlServerLockStatementProvider();
                })
                .Endpoint(e =>
                {
                    e.ConcurrentMessageLimit = sagaOptions.ConcurrencyLimit;
                    e.PrefetchCount = sagaOptions.ConcurrencyLimit * 2;
                });

            cfg.AddSagaStateMachine<PaymentProcessingSaga, PaymentSagaState>()
                .EntityFrameworkRepository(r =>
                {
                    r.ConcurrencyMode = ConcurrencyMode.Optimistic;
                    r.ExistingDbContext<SubscriptionSagaDbContext>();
                    r.LockStatementProvider = new SqlServerLockStatementProvider();
                })
                .Endpoint(e =>
                {
                    e.ConcurrentMessageLimit = sagaOptions.ConcurrencyLimit;
                    e.PrefetchCount = sagaOptions.ConcurrencyLimit * 2;
                });

            cfg.AddKafkaRider(sagaOptions);

            cfg.UsingSqlServer((context, busCfg) =>
            {
                busCfg.UseSqlMessageScheduler();

                busCfg.UseMessageRetry(r => r.Intervals(
                    TimeSpan.FromSeconds(sagaOptions.RetryDelaySeconds),
                    TimeSpan.FromSeconds(sagaOptions.RetryDelaySeconds * 2),
                    TimeSpan.FromSeconds(sagaOptions.RetryDelaySeconds * 4)));

                busCfg.ConfigureEndpoints(context);
            });
        });

        services.AddStateObserver<SubscriptionPurchaseSagaState, SubscriptionSagaStateObserver>();
        services.AddStateObserver<SubscriptionExtensionSagaState, SubscriptionExtensionSagaStateObserver>();
        services.AddStateObserver<PaymentSagaState, PaymentSagaStateObserver>();

        return services;
    }

    private static void AddSagaDatabase(
        this IServiceCollection services,
        IConfiguration configuration,
        EfCoreOptions options)
    {
        services.AddPooledDbContextFactory<SubscriptionSagaDbContext>((_, dbOptions) =>
        {
            dbOptions.UseSqlServer(
                configuration.GetConnectionString(nameof(ConnectionStringsOptions.Saga)),
                sqlOptions =>
                {
                    sqlOptions.EnableRetryOnFailure(
                        maxRetryCount: options.RetryMaxCount,
                        maxRetryDelay: TimeSpan.FromSeconds(options.RetryMaxDelaySeconds),
                        errorNumbersToAdd: null);
                });

            if (options.EnableDetailedErrors)
            {
                dbOptions.EnableDetailedErrors();
            }
        }, poolSize: options.DbContextPoolSize);

        // Also register the DbContext itself for consumers that need direct injection
        services.AddScoped(sp =>
            sp.GetRequiredService<IDbContextFactory<SubscriptionSagaDbContext>>().CreateDbContext());
    }

    private static void AddKafkaRider(
        this IBusRegistrationConfigurator cfg,
        SagaOptions options)
    {
        cfg.AddRider(rider =>
        {
            rider.AddSagaKafkaConsumers();
            rider.AddSagaKafkaProducers(options);
            rider.UsingKafka((registrationContext, kafkaFactoryConfigurator) =>
            {
                kafkaFactoryConfigurator.Host(options.KafkaBootstrapServers);

                // Configure all saga topic endpoints
                kafkaFactoryConfigurator.ConfigureSagaTopicEndpoints(registrationContext, options);
            });
        });
    }
}
