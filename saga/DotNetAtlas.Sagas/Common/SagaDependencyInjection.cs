using Confluent.SchemaRegistry;
using DotNetAtlas.ReliableMessaging.Outbox.EFCore.Common;
using DotNetAtlas.Sagas.Common.Config;
using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Common.SagasDependencyInjection;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Consumers;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.Consumers;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Consumers;
using DotNetAtlas.Sagas.Persistence.Database.Interceptors;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using SagaDbContext = DotNetAtlas.Sagas.Persistence.Database.SagaDbContext;

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
            services.AddOptionsWithValidateOnStart<SagaOptions>()
                .BindConfiguration(SagaOptions.Section)
                .ValidateDataAnnotations();

            services.AddOptionsWithValidateOnStart<SagaKafkaOptions>()
                .BindConfiguration(SagaKafkaOptions.Section)
                .ValidateDataAnnotations();

            var sagaOptions = configuration
                .GetRequiredSection(SagaOptions.Section)
                .Get<SagaOptions>()!;

            var sagaKafkaOptions = configuration
                .GetRequiredSection(SagaKafkaOptions.Section)
                .Get<SagaKafkaOptions>()!;

            services.AddSingleton<ISchemaRegistryClient>(_ =>
                new CachedSchemaRegistryClient(new SchemaRegistryConfig
                {
                    Url = sagaKafkaOptions.SchemaRegistry.Url
                }));

            var connectionStringsOptions = configuration
                .GetRequiredSection(ConnectionStringsOptions.Section)
                .Get<ConnectionStringsOptions>()!;

            services.AddOptions<SqlTransportOptions>()
                .Configure(options =>
                {
                    options.ConnectionString = connectionStringsOptions.Saga;
                });

            services.AddSqlServerMigrationHostedService();

            services.AddMassTransit(cfg =>
            {
                cfg.SetKebabCaseEndpointNameFormatter();

                cfg.AddSagaStateMachine<AlertSubscriptionPurchaseSaga, AlertSubscriptionPurchaseSagaState>()
                    .EntityFrameworkRepository(r =>
                    {
                        r.ConcurrencyMode = ConcurrencyMode.Optimistic;
                        r.ExistingDbContext<SagaDbContext>();
                        r.LockStatementProvider = new SqlServerLockStatementProvider();
                    })
                    .Endpoint(e =>
                    {
                        e.ConcurrentMessageLimit = sagaOptions.ConcurrencyLimit;
                        e.PrefetchCount = sagaOptions.ConcurrencyLimit * 2;
                    });

                cfg.AddSagaStateMachine<AlertSubscriptionExtensionSaga, AlertSubscriptionExtensionSagaState>()
                    .EntityFrameworkRepository(r =>
                    {
                        r.ConcurrencyMode = ConcurrencyMode.Optimistic;
                        r.ExistingDbContext<SagaDbContext>();
                        r.LockStatementProvider = new SqlServerLockStatementProvider();
                    })
                    .Endpoint(e =>
                    {
                        e.ConcurrentMessageLimit = sagaOptions.ConcurrencyLimit;
                        e.PrefetchCount = sagaOptions.ConcurrencyLimit * 2;
                    });

                cfg.AddSagaStateMachine<PaymentProcessingSaga, PaymentProcessingSagaState>()
                    .EntityFrameworkRepository(r =>
                    {
                        r.ConcurrencyMode = ConcurrencyMode.Optimistic;
                        r.ExistingDbContext<SagaDbContext>();
                        r.LockStatementProvider = new SqlServerLockStatementProvider();
                    })
                    .Endpoint(e =>
                    {
                        e.ConcurrentMessageLimit = sagaOptions.ConcurrencyLimit;
                        e.PrefetchCount = sagaOptions.ConcurrencyLimit * 2;
                    });

                cfg.AddSagaKafkaRider(sagaKafkaOptions);

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

        private void AddSagaDatabase(
            IConfiguration configuration,
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

            var sagaKafkaOptions = configuration
                .GetRequiredSection(SagaKafkaOptions.Section)
                .Get<SagaKafkaOptions>()!;

            services.AddOutbox(outbox =>
            {
                outbox.ConfigureMessageOrigin(ApplicationInfo.AppName);
                outbox.ConfigureAvroSerializerConfig(config =>
                {
                    config.NormalizeSchemas = true;
                    config.AutoRegisterSchemas = true;
                    config.SubjectNameStrategy = SubjectNameStrategy.Record;
                });
                outbox.ConfigureSchemaRegistryConfig(config =>
                {
                    config.Url = sagaKafkaOptions.SchemaRegistry.Url;
                });
            });
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<UpdateSagaAuditableEntitiesInterceptor>();
            services.AddDbContext<SagaDbContext>((
                sp,
                options) => options
                .UseSqlServer(
                    configuration.GetConnectionString(nameof(ConnectionStringsOptions.Saga)),
                    sqlServerOptions =>
                    {
                        sqlServerOptions.MigrationsHistoryTable(HistoryRepository.DefaultTableName,
                            SagaDbContext.DefaultSchemaName);
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
        private void AddSagaKafkaRider(SagaKafkaOptions sagaKafkaOptions)
        {
            cfg.AddRider(rider =>
            {
                rider.AddConsumersFromNamespaceContaining<AlertSubscriptionPurchaseInitiatedConsumer>();
                rider.AddConsumersFromNamespaceContaining<AlertSubscriptionExtensionInitiatedConsumer>();
                rider.AddConsumersFromNamespaceContaining<PaymentRequestedConsumer>();

                rider.UsingKafka((registrationContext, kafkaConfigurator) =>
                {
                    kafkaConfigurator.Host(sagaKafkaOptions.BrokersFlat);

                    var schemaRegistryClient = registrationContext.GetRequiredService<ISchemaRegistryClient>();
                    kafkaConfigurator.ConfigureAlertSubscriptionPurchaseSagaConsumers(registrationContext,
                        schemaRegistryClient, sagaKafkaOptions);
                    kafkaConfigurator.ConfigureAlertSubscriptionExtensionSagaConsumers(registrationContext,
                        schemaRegistryClient, sagaKafkaOptions);
                    kafkaConfigurator.ConfigurePaymentSagaConsumers(registrationContext, schemaRegistryClient,
                        sagaKafkaOptions);
                });
            });
        }
    }
}
