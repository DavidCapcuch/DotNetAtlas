using Confluent.SchemaRegistry;
using DotNetAtlas.Sagas.Common.Config;
using DotNetAtlas.Sagas.Finance.PaymentSaga;
using DotNetAtlas.Sagas.Orders.ExtendAlertSubscriptionSaga;
using DotNetAtlas.Sagas.Orders.PurchaseAlertSubscriptionSaga;
using DotNetAtlas.Sagas.Persistence.Database;
using DotNetAtlas.Sagas.Persistence.Database.Interceptors;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DotNetAtlas.Sagas.Common;

/// <summary>
/// Extension methods for registering saga services.
/// </summary>
public static class SagaDependencyInjection
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddSagaOrchestration(IConfiguration configuration,
            bool isClusterEnvironment)
        {
            services.AddSagaDatabase(configuration, isClusterEnvironment);
            services.AddSagaStateMachines(configuration);

            return services;
        }

        private IServiceCollection AddSagaStateMachines(IConfiguration configuration)
        {
            var sagaOptions = configuration
                .GetRequiredSection(SagaOptions.Section)
                .Get<SagaOptions>()!;

            services.AddSingleton<ISchemaRegistryClient>(_ =>
                new CachedSchemaRegistryClient(new SchemaRegistryConfig
                {
                    Url = sagaOptions.SchemaRegistryUrl
                }));

            services.AddOptionsWithValidateOnStart<SagaOptions>()
                .BindConfiguration(SagaOptions.Section)
                .ValidateDataAnnotations();

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

                cfg.AddSagaKafkaRider(sagaOptions);

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

            return services;
        }

        private void AddSagaDatabase(IConfiguration configuration,
            bool isClusterEnvironment)
        {
            services.AddOptionsWithValidateOnStart<ConnectionStringsOptions>()
                .BindConfiguration(ConnectionStringsOptions.Section)
                .ValidateDataAnnotations();

            services.AddOptionsWithValidateOnStart<EfCoreOptions>()
                .BindConfiguration(EfCoreOptions.Section)
                .ValidateDataAnnotations();

            var efCoreOptions = configuration
                .GetRequiredSection(EfCoreOptions.Section)
                .Get<EfCoreOptions>()!;

            services.AddSingleton<UpdateSagaAuditableEntitiesInterceptor>();
            services.AddDbContext<SubscriptionSagaDbContext>((
                sp,
                options) => options
                .UseSqlServer(
                    configuration.GetConnectionString(nameof(ConnectionStringsOptions.Saga)),
                    sqlServerOptions =>
                    {
                        sqlServerOptions.MigrationsHistoryTable(HistoryRepository.DefaultTableName,
                            SubscriptionSagaDbContext.DefaultSchemaName);
                        sqlServerOptions.EnableRetryOnFailure(
                            maxRetryCount: efCoreOptions.RetryMaxCount,
                            maxRetryDelay: TimeSpan.FromSeconds(efCoreOptions.RetryMaxDelaySeconds),
                            errorNumbersToAdd: null);
                    })
                .EnableSensitiveDataLogging(
                    !isClusterEnvironment) // this is very useful for local debugging/investigating failed tests
                .EnableDetailedErrors(efCoreOptions.EnableDetailedErrors)
                .AddInterceptors(
                    sp.GetRequiredService<UpdateSagaAuditableEntitiesInterceptor>()));
        }
    }

    extension(IBusRegistrationConfigurator cfg)
    {
        private void AddSagaKafkaRider(SagaOptions sagaOptions)
        {
            cfg.AddRider(rider =>
            {
                rider.AddSubscriptionPurchaseSagaConsumers();
                rider.AddSubscriptionPurchaseSagaProducers(sagaOptions);

                rider.AddSubscriptionExtensionSagaConsumers();
                rider.AddSubscriptionExtensionSagaProducers(sagaOptions);

                rider.AddPaymentSagaConsumers();
                rider.AddPaymentSagaProducers(sagaOptions);

                rider.UsingKafka((registrationContext, kafkaFactoryConfigurator) =>
                {
                    kafkaFactoryConfigurator.Host(sagaOptions.KafkaBootstrapServers);

                    var schemaRegistryClient = registrationContext.GetRequiredService<ISchemaRegistryClient>();
                    kafkaFactoryConfigurator.ConfigureSubscriptionPurchaseSagaEndpoints(registrationContext,
                        schemaRegistryClient, sagaOptions);
                    kafkaFactoryConfigurator.ConfigureSubscriptionExtensionSagaEndpoints(registrationContext,
                        schemaRegistryClient, sagaOptions);
                    kafkaFactoryConfigurator.ConfigurePaymentSagaEndpoints(registrationContext, schemaRegistryClient,
                        sagaOptions);
                });
            });
        }
    }
}
