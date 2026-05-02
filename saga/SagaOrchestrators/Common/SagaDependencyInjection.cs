using Confluent.SchemaRegistry;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Platform.ReliableMessaging.Outbox.EFCore.Common;
using SagaOrchestrators.Checkout.CheckoutSaga;
using SagaOrchestrators.Common.Config;
using SagaOrchestrators.Common.Config.Kafka;
using SagaOrchestrators.Common.Observability;
using SagaOrchestrators.Common.Persistence.Database.Interceptors;
using SagaOrchestrators.Common.SagasDependencyInjection;
using SagaOrchestrators.Payments.PaymentProcessingSaga;
using SagaOrchestrators.Payments.PaymentProcessingSaga.Consumers;
using SagaDbContext = SagaOrchestrators.Common.Persistence.Database.SagaDbContext;

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

            services.AddPostgresMigrationHostedService();

            services.AddMassTransit(cfg =>
            {
                cfg.SetKebabCaseEndpointNameFormatter();

                cfg.AddSagaStateMachine<PaymentProcessingSagaOrchestrator, PaymentProcessingSagaState>()
                    .EntityFrameworkRepository(r =>
                    {
                        r.ConcurrencyMode = ConcurrencyMode.Optimistic;
                        r.ExistingDbContext<SagaDbContext>();
                        r.UsePostgres();
                    })
                    .Endpoint(e =>
                    {
                        e.ConcurrentMessageLimit = sagaOptions.ConcurrencyLimit;
                        e.PrefetchCount = sagaOptions.ConcurrencyLimit * 2;
                    });

                cfg.AddSagaStateMachine<CheckoutSagaOrchestrator, CheckoutSagaState>()
                    .EntityFrameworkRepository(r =>
                    {
                        r.ConcurrencyMode = ConcurrencyMode.Optimistic;
                        r.ExistingDbContext<SagaDbContext>();
                        r.UsePostgres();
                    })
                    .Endpoint(e =>
                    {
                        e.ConcurrentMessageLimit = sagaOptions.ConcurrencyLimit;
                        e.PrefetchCount = sagaOptions.ConcurrencyLimit * 2;
                    });

                cfg.AddSagaKafkaRider(sagaKafkaOptions);

                cfg.UsingPostgres((context, busCfg) =>
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
            services.AddDbContext<SagaDbContext>((
                sp,
                options) => options
                .UseNpgsql(
                    configuration.GetConnectionString(nameof(ConnectionStringsOptions.Saga)),
                    npgsqlOptions =>
                    {
                        npgsqlOptions.MigrationsHistoryTable(HistoryRepository.DefaultTableName,
                            SagaDbContext.DefaultSchemaName);
                        npgsqlOptions.EnableRetryOnFailure(
                            maxRetryCount: efCoreOptions.RetryMaxCount,
                            maxRetryDelay: TimeSpan.FromSeconds(efCoreOptions.RetryMaxDelaySeconds),
                            errorCodesToAdd: null);
                    })
                .UseSnakeCaseNamingConvention()
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
                rider.AddConsumersFromNamespaceContaining<PaymentRequestedConsumer>();

                rider.UsingKafka((registrationContext, kafkaConfigurator) =>
                {
                    kafkaConfigurator.Host(kafkaOptions.BrokersFlat);

                    var schemaRegistryClient = registrationContext.GetRequiredService<ISchemaRegistryClient>();
                    kafkaConfigurator.ConfigurePaymentSagaConsumers(registrationContext, schemaRegistryClient,
                        kafkaOptions);
                });
            });
        }
    }
}
