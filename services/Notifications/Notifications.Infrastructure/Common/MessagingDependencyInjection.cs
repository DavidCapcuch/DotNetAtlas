using Confluent.Kafka;
using KafkaFlow;
using KafkaFlow.Configuration;
using KafkaFlow.Retry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Application.Common.Messaging;
using Notifications.Application.Dispatch;
using Notifications.Application.Email;
using Notifications.Application.Recipients;
using Notifications.Domain.Channels;
using Notifications.Infrastructure.Common.Config;
using Notifications.Infrastructure.Common.Observability;
using Notifications.Infrastructure.Dispatch;
using Notifications.Infrastructure.Email;
using Notifications.Infrastructure.NotifyUser;
using Notifications.Infrastructure.Persistence.Database;
using Notifications.Infrastructure.Recipients;
using Platform.KafkaFlow.DeadLetter;
using Platform.KafkaFlow.DeadLetter.Common;
using Platform.KafkaFlow.Inbox.EFCore.Common;
using Platform.KafkaFlow.ProducerHeaders;
using Platform.ReliableMessaging.Inbox.EFCore.Common;
using Platform.ReliableMessaging.Outbox.EFCore.Common;

namespace Notifications.Infrastructure.Common;

/// <summary>
/// DI wiring for Kafka — the inbound <c>TopicsOptions.NotifyCommands</c> consumer that dispatches
/// <c>NotifyUserCommand</c> to <see cref="NotifyUserCommandKafkaHandler"/>, the inbox dedup adapter
/// against <see cref="NotificationsDbContext"/>, the channel dispatchers (Keyed DI by
/// <see cref="ChannelType"/>; only email this slice), and the transactional-outbox writer for
/// <c>NotificationDeliveryStatusChangedEvent</c> publishing (<c>TopicsOptions.NotifyEvents</c>).
/// See ADR-0031/0032.
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

        services.AddOptionsWithValidateOnStart<NotifyCommandsConsumerOptions>()
            .BindConfiguration(NotifyCommandsConsumerOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<SmtpOptions>()
            .BindConfiguration(SmtpOptions.Section)
            .ValidateDataAnnotations();

        var kafkaOptions = configuration
            .GetRequiredSection(KafkaOptions.Section)
            .Get<KafkaOptions>()!;

        var consumerOptions = configuration
            .GetRequiredSection(NotifyCommandsConsumerOptions.Section)
            .Get<NotifyCommandsConsumerOptions>()!;
        consumerOptions.PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky;

        var topicsOptions = configuration
            .GetRequiredSection(TopicsOptions.Section)
            .Get<TopicsOptions>()!;

        // Email channel collaborators. SmtpEmailGateway → Mailpit is the live transport;
        // MockEmailGateway is retained for unit tests (constructed directly, not via DI). Rendering
        // uses the pure Domain TemplateRenderer over template_channels (no DI registration; #313).
        services.AddScoped<IEmailGateway, SmtpEmailGateway>();
        services.AddScoped<IRecipientResolver, StubRecipientResolver>();

        // Channel dispatchers in Keyed DI by ChannelType. Only the email channel is wired in the
        // walking skeleton (#312); SMS/bell dispatchers register additional keys in later slices.
        services.AddKeyedScoped<IChannelDispatcher, EmailChannelDispatcher>(ChannelType.Email);
        services.AddScoped<NotificationDispatchJob>();
        services.AddSingleton<IChannelDispatchEnqueuer, HangfireChannelDispatchEnqueuer>();

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
                    .Topic(topicsOptions.NotifyCommands)
                    .WithConsumerConfig(consumerOptions)
                    .WithBufferSize(consumerOptions.BufferSize)
                    .WithWorkersCount(consumerOptions.WorkersCount)
                    .AddMiddlewares(middlewares => middlewares
                        .AddSchemaRegistryAvroDeserializer()
                        // Middleware order -> outermost to innermost
                        .AddDeadLetter()
                        .RetryForever(config => config
                            .Handle(ctx => ConsumerRetry.IsRetryable(ctx.Exception))
                            .WithTimeBetweenTriesPlan(
                                TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1),
                                TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)))
                        .AddInbox(typeof(NotifyUserCommand))
                        .AddTypedHandlers(handlers => handlers
                            .WithHandlerLifetime(InstanceLifetime.Scoped)
                            .AddHandler<NotifyUserCommandKafkaHandler>())
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
