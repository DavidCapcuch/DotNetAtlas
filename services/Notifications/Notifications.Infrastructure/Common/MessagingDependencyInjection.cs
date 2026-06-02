using KafkaFlow;
using KafkaFlow.Configuration;
using KafkaFlow.Retry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Application.Common.Messaging;
using Notifications.Application.Email;
using Notifications.Email;
using Notifications.Infrastructure.Common.Config;
using Notifications.Infrastructure.Common.Observability;
using Notifications.Infrastructure.Email;
using Notifications.Infrastructure.Persistence.Database;
using Notifications.Infrastructure.SendEmailNotification;
using Platform.KafkaFlow.DeadLetter;
using Platform.KafkaFlow.DeadLetter.Common;
using Platform.KafkaFlow.Inbox.EFCore.Common;
using Platform.KafkaFlow.ProducerHeaders;
using Platform.ReliableMessaging.Inbox.EFCore.Common;
using Platform.ReliableMessaging.Outbox.EFCore.Common;

namespace Notifications.Infrastructure.Common;

/// <summary>
/// DI wiring for Kafka — the inbound <c>TopicsOptions.EmailCommands</c> consumer that
/// dispatches <c>SendEmailNotificationCommand</c> to <see cref="SendEmailNotificationCommandKafkaHandler"/>,
/// the inbox dedup adapter against <see cref="NotificationsDbContext"/>, and the transactional-outbox
/// writer for <c>EmailNotificationSentEvent</c> publishing (<c>TopicsOptions.EmailEvents</c>).
/// </summary>
internal static class MessagingDependencyInjection
{
    /// <summary>
    /// The origin identifier used in Kafka message headers to identify this service.
    /// </summary>
    private const string KafkaProducerOrigin = ApplicationInfo.AppName;

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

        services.AddOptionsWithValidateOnStart<SchemaRegistryOptions>()
            .BindConfiguration(SchemaRegistryOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<AvroSerializerOptions>()
            .BindConfiguration(AvroSerializerOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<EmailCommandsConsumerOptions>()
            .BindConfiguration(EmailCommandsConsumerOptions.Section)
            .ValidateDataAnnotations();

        var kafkaOptions = configuration
            .GetRequiredSection(KafkaOptions.Section)
            .Get<KafkaOptions>()!;

        var consumerOptions = configuration
            .GetRequiredSection(EmailCommandsConsumerOptions.Section)
            .Get<EmailCommandsConsumerOptions>()!;

        var topicsOptions = configuration
            .GetRequiredSection(TopicsOptions.Section)
            .Get<TopicsOptions>()!;

        services.AddScoped<IEmailGateway, MockEmailGateway>();
        services.AddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();

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
                    .Topic(topicsOptions.EmailCommands)
                    .WithConsumerConfig(consumerOptions.WithCooperativeRebalancing())
                    .WithBufferSize(consumerOptions.BufferSize)
                    .WithWorkersCount(consumerOptions.WorkersCount)
                    .AddMiddlewares(middlewares => middlewares
                        .AddSchemaRegistryAvroDeserializer()
                        // Middleware order -> outermost to innermost
                        .AddCorrelationIdConsumerMiddleware()
                        .AddDeadLetter()
                        .RetryForever(config => config
                            .Handle(ctx => ConsumerRetry.IsRetryable(ctx.Exception))
                            .WithTimeBetweenTriesPlan(
                                TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1),
                                TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)))
                        .AddInbox(typeof(SendEmailNotificationCommand))
                        .AddTypedHandlers(handlers => handlers
                            .WithHandlerLifetime(InstanceLifetime.Scoped)
                            .AddHandler<SendEmailNotificationCommandKafkaHandler>())
                    )
                ))
            .UseMicrosoftLog()
            .AddOpenTelemetryInstrumentation());

        services.AddInbox<NotificationsDbContext>();
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
