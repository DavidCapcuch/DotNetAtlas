using DotNetAtlas.KafkaFlow.DeadLetter.Common;
using DotNetAtlas.KafkaFlow.Inbox.EFCore.Common;
using DotNetAtlas.KafkaFlow.ProducerHeaders;
using DotNetAtlas.ReliableMessaging.Inbox.EFCore.Common;
using DotNetAtlas.ReliableMessaging.Outbox.EFCore.Common;
using KafkaFlow;
using KafkaFlow.Configuration;
using KafkaFlow.Retry;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Payments.Common.Config;
using Payments.Common.Observability;
using Payments.Payments.AuthorizePayment;
using Payments.Persistence.Database;
using Weather.Alerts;

namespace Payments.Common;

/// <summary>
/// Dependency injection extensions for communication infrastructure.
/// </summary>
internal static class MessagingDependencyInjection
{
    /// <summary>
    /// The origin identifier used in Kafka message headers to identify this service.
    /// </summary>
    private const string KafkaProducerOrigin = ApplicationInfo.AppName;

    /// <summary>
    /// Configures Kafka messaging with producers and schema registry.
    /// Sets up event-driven messaging infrastructure.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration manager.</param>
    /// <returns>The service collection for chaining.</returns>
    internal static IServiceCollection AddKafkaMessaging(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        services.AddOptionsWithValidateOnStart<KafkaOptions>()
            .BindConfiguration(KafkaOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<TopicsOptions>()
            .BindConfiguration(TopicsOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<KafkaConsumerOptions>()
            .BindConfiguration(KafkaConsumerOptions.Section)
            .ValidateDataAnnotations();

        var kafkaOptions = configuration
            .GetRequiredSection(KafkaOptions.Section)
            .Get<KafkaOptions>()!;

        var consumerOptions = configuration
            .GetRequiredSection(KafkaConsumerOptions.Section)
            .Get<KafkaConsumerOptions>()!;

        var topicsOptions = configuration
            .GetRequiredSection(TopicsOptions.Section)
            .Get<TopicsOptions>()!;

        services.AddKafka(kafka => kafka
            .AddCluster(cluster => cluster
                .WithBrokers(kafkaOptions.Brokers)
                .WithSchemaRegistry(config => config.Url = kafkaOptions.SchemaRegistry.Url)
                .AddDltProducer(
                    topicsOptions.DltTopicSuffix,
                    producer => producer
                        .AddMiddlewares(m => m
                            .AddProducerHeaders(KafkaProducerOrigin)
                            .AddSchemaRegistryAvroSerializer(kafkaOptions.AvroSerializer)))
                .AddConsumer(consumer => consumer
                    .Topic(topicsOptions.PaymentCommands)
                    .WithConsumerConfig(consumerOptions)
                    .WithBufferSize(consumerOptions.BufferSize)
                    .WithWorkersCount(consumerOptions.WorkersCount)
                    .AddMiddlewares(middlewares => middlewares
                        .AddSchemaRegistryAvroDeserializer()
                        // Middleware order -> outermost to innermost
                        .AddDeadLetter()
                        .RetryForever(config => config
                            .Handle<DbUpdateException>()
                            .Handle<SqlException>()
                            .Handle<TimeoutException>()
                            .WithTimeBetweenTriesPlan(
                                TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1),
                                TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)))
                        .AddInbox(typeof(ActivateAlertSubscriptionCommand), typeof(ExtendAlertSubscriptionCommand))
                        .AddTypedHandlers(handlers => handlers
                            .WithHandlerLifetime(InstanceLifetime.Scoped)
                            .AddHandler<AuthorizePaymentCommandKafkaHandler>())
                    )
                ))
            .UseMicrosoftLog()
            .AddOpenTelemetryInstrumentation());

        services.AddInbox<PaymentDbContext>();
        services.AddOutbox(outbox =>
        {
            outbox.ConfigureMessageOrigin(ApplicationInfo.AppName);

            outbox.ConfigureAvroSerializerConfig(options =>
            {
                configuration.Bind(AvroSerializerOptions.Section, options);
            });

            outbox.ConfigureSchemaRegistryConfig(options =>
            {
                configuration.Bind(SchemaRegistryOptions.Section, options);
            });
        });

        return services;
    }
}
