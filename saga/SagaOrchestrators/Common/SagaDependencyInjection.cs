using Confluent.SchemaRegistry;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Platform.ReliableMessaging.Outbox.EFCore.Common;
using SagaOrchestrators.Common.Config;
using SagaOrchestrators.Common.Config.Kafka;
using SagaOrchestrators.Common.Observability;
using SagaOrchestrators.Common.SagasDependencyInjection;
using SagaOrchestrators.Finance.PaymentProcessingSaga;
using SagaOrchestrators.Finance.PaymentProcessingSaga.Consumers;
using SagaOrchestrators.Orders.AlertSubscriptionExtensionSaga;
using SagaOrchestrators.Orders.AlertSubscriptionExtensionSaga.Consumers;
using SagaOrchestrators.Orders.AlertSubscriptionPurchaseSaga;
using SagaOrchestrators.Orders.AlertSubscriptionPurchaseSaga.Consumers;
using SagaOrchestrators.Persistence.Database.Interceptors;
using Database_SagaDbContext = SagaOrchestrators.Persistence.Database.SagaDbContext;

namespace SagaOrchestrators.Common;

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

            services.AddOptionsWithValidateOnStart<KafkaOptions>()
                .BindConfiguration(KafkaOptions.Section)
                .ValidateDataAnnotations();

            services.AddOptionsWithValidateOnStart<SagaTopicsOptions>()
                .BindConfiguration(SagaTopicsOptions.Section)
                .ValidateDataAnnotations();

            var sagaOptions = configuration
                .GetRequiredSection(SagaOptions.Section)
                .Get<SagaOptions>()!;

            var sagaKafkaOptions = configuration
                .GetRequiredSection(KafkaOptions.Section)
                .Get<KafkaOptions>()!;

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

                cfg.AddSagaStateMachine<AlertSubscriptionPurchaseSagaOrchestrator, AlertSubscriptionPurchaseSagaState>()
                    .EntityFrameworkRepository(r =>
                    {
                        r.ConcurrencyMode = ConcurrencyMode.Optimistic;
                        r.ExistingDbContext<Database_SagaDbContext>();
                        r.LockStatementProvider = new SqlServerLockStatementProvider();
                    })
                    .Endpoint(e =>
                    {
                        e.ConcurrentMessageLimit = sagaOptions.ConcurrencyLimit;
                        e.PrefetchCount = sagaOptions.ConcurrencyLimit * 2;
                    });

                cfg.AddSagaStateMachine<AlertSubscriptionExtensionSagaOrchestrator, AlertSubscriptionExtensionSagaState>()
                    .EntityFrameworkRepository(r =>
                    {
                        r.ConcurrencyMode = ConcurrencyMode.Optimistic;
                        r.ExistingDbContext<Database_SagaDbContext>();
                        r.LockStatementProvider = new SqlServerLockStatementProvider();
                    })
                    .Endpoint(e =>
                    {
                        e.ConcurrentMessageLimit = sagaOptions.ConcurrencyLimit;
                        e.PrefetchCount = sagaOptions.ConcurrencyLimit * 2;
                    });

                cfg.AddSagaStateMachine<PaymentProcessingSagaOrchestrator, PaymentProcessingSagaState>()
                    .EntityFrameworkRepository(r =>
                    {
                        r.ConcurrencyMode = ConcurrencyMode.Optimistic;
                        r.ExistingDbContext<Database_SagaDbContext>();
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
                .GetRequiredSection(KafkaOptions.Section)
                .Get<KafkaOptions>()!;

            services.AddOutbox(outbox =>
            {
                outbox.ConfigureMessageOrigin(ApplicationInfo.AppName);
                outbox.ConfigureAvroSerializerConfig(options =>
                {
                    configuration.Bind(AvroSerializerOptions.Section, options);
                });
                outbox.ConfigureSchemaRegistryConfig(config =>
                {
                    config.Url = sagaKafkaOptions.SchemaRegistry.Url;
                });
            });
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<UpdateAuditableEntitiesInterceptor>();
            services.AddDbContext<Database_SagaDbContext>((
                sp,
                options) => options
                .UseSqlServer(
                    configuration.GetConnectionString(nameof(ConnectionStringsOptions.Saga)),
                    sqlServerOptions =>
                    {
                        sqlServerOptions.MigrationsHistoryTable(HistoryRepository.DefaultTableName,
                            Database_SagaDbContext.DefaultSchemaName);
                        sqlServerOptions.EnableRetryOnFailure(
                            maxRetryCount: efCoreOptions.RetryMaxCount,
                            maxRetryDelay: TimeSpan.FromSeconds(efCoreOptions.RetryMaxDelaySeconds),
                            errorNumbersToAdd: null);
                    })
                .EnableSensitiveDataLogging(
                    !isClusterEnvironment) // this is very useful for local debugging/investigating failed tests
                .EnableDetailedErrors(efCoreOptions.EnableDetailedErrors)
                .AddInterceptors(
                    sp.GetRequiredService<UpdateAuditableEntitiesInterceptor>()));
        }
    }

    extension(IBusRegistrationConfigurator cfg)
    {
        private void AddSagaKafkaRider(KafkaOptions kafkaOptions)
        {
            cfg.AddRider(rider =>
            {
                rider.AddConsumersFromNamespaceContaining<AlertSubscriptionPurchaseInitiatedConsumer>();
                rider.AddConsumersFromNamespaceContaining<AlertSubscriptionExtensionInitiatedConsumer>();
                rider.AddConsumersFromNamespaceContaining<PaymentRequestedConsumer>();

                rider.UsingKafka((registrationContext, kafkaConfigurator) =>
                {
                    kafkaConfigurator.Host(kafkaOptions.BrokersFlat);

                    var schemaRegistryClient = registrationContext.GetRequiredService<ISchemaRegistryClient>();
                    kafkaConfigurator.ConfigureAlertSubscriptionPurchaseSagaConsumers(registrationContext,
                        schemaRegistryClient, kafkaOptions);
                    kafkaConfigurator.ConfigureAlertSubscriptionExtensionSagaConsumers(registrationContext,
                        schemaRegistryClient, kafkaOptions);
                    kafkaConfigurator.ConfigurePaymentSagaConsumers(registrationContext, schemaRegistryClient,
                        kafkaOptions);
                });
            });
        }
    }
}
