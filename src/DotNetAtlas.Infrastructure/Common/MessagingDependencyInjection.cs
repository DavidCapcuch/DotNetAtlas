using AspNetCore.SignalR.OpenTelemetry;
using DotNetAtlas.Application.Common.Observability;
using DotNetAtlas.Application.WeatherForecast.Common;
using DotNetAtlas.Infrastructure.Common.Config;
using DotNetAtlas.Infrastructure.Messaging.Kafka.Config;
using DotNetAtlas.Infrastructure.Messaging.Kafka.Dev;
using DotNetAtlas.Infrastructure.Messaging.Kafka.Subscriptions;
using DotNetAtlas.Infrastructure.Messaging.Kafka.WeatherForecastEvents;
using DotNetAtlas.Infrastructure.Persistence.Database;
using DotNetAtlas.KafkaFlow.DeadLetter.Common;
using DotNetAtlas.KafkaFlow.Inbox.EFCore.Common;
using DotNetAtlas.KafkaFlow.ProducerHeaders;
using DotNetAtlas.ReliableMessaging.Inbox.EFCore.Common;
using DotNetAtlas.ReliableMessaging.Outbox.EFCore.Common;
using KafkaFlow;
using KafkaFlow.Configuration;
using KafkaFlow.Retry;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Weather.Alerts;

namespace DotNetAtlas.Infrastructure.Common;

/// <summary>
/// Dependency injection extensions for communication infrastructure.
/// Configures Kafka messaging and SignalR real-time communication.
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

        services.AddOptionsWithValidateOnStart<ForecastEventsKafkaProducerOptions>()
            .BindConfiguration(ForecastEventsKafkaProducerOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<SubscriptionsKafkaConsumerOptions>()
            .BindConfiguration(SubscriptionsKafkaConsumerOptions.Section)
            .ValidateDataAnnotations();

        var kafkaOptions = configuration
            .GetRequiredSection(KafkaOptions.Section)
            .Get<KafkaOptions>()!;

        var producerOptions = configuration
            .GetRequiredSection(ForecastEventsKafkaProducerOptions.Section)
            .Get<ForecastEventsKafkaProducerOptions>()!;

        var consumerOptions = configuration
            .GetRequiredSection(SubscriptionsKafkaConsumerOptions.Section)
            .Get<SubscriptionsKafkaConsumerOptions>()!;

        var topicsOptions = configuration
            .GetRequiredSection(TopicsOptions.Section)
            .Get<TopicsOptions>()!;

        services.AddKafka(kafka => kafka
            .AddCluster(cluster => cluster
                .WithBrokers(kafkaOptions.Brokers)
                .WithSchemaRegistry(config => config.Url = kafkaOptions.SchemaRegistry.Url)
                .AddProducer<ForecastEventsKafkaProducer>(producer =>
                    producer
                        .WithProducerConfig(producerOptions)
                        .AddMiddlewares(m => m
                            .AddProducerHeaders(KafkaProducerOrigin)
                            .AddSchemaRegistryAvroSerializer(kafkaOptions.AvroSerializer))
                )
                .AddProducer<DevEventsKafkaProducer>(producer =>
                    producer
                        .WithProducerConfig(producerOptions)
                        .AddMiddlewares(m => m
                            .AddProducerHeaders(KafkaProducerOrigin)
                            .AddSchemaRegistryAvroSerializer(kafkaOptions.AvroSerializer))
                )
                .AddDltProducer(
                    topicsOptions.DltTopicSuffix,
                    producer => producer
                        .AddMiddlewares(m => m
                            .AddProducerHeaders(KafkaProducerOrigin)
                            .AddSchemaRegistryAvroSerializer(kafkaOptions.AvroSerializer)))
                .AddConsumer(consumer => consumer
                    .Topic(topicsOptions.WeatherAlertsCommands)
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
                        .AddInbox(typeof(ActivateSubscriptionCommand), typeof(ExtendSubscriptionCommand))
                        .AddTypedHandlers(handlers => handlers
                            .WithHandlerLifetime(InstanceLifetime.Scoped)
                            .AddHandler<ActivateSubscriptionCommandKafkaHandler>()
                            .AddHandler<ExtendSubscriptionCommandKafkaHandler>())
                    )
                ))
            .UseMicrosoftLog()
            .AddOpenTelemetryInstrumentation());

        services.AddInbox<WeatherDbContext>();
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

        services.AddSingleton<IForecastEventsProducer, ForecastEventsKafkaProducer>();
        services.AddSingleton<DevEventsKafkaProducer>();

        return services;
    }

    /// <summary>
    /// Configures SignalR hubs with Redis backplane for horizontal scaling.
    /// Sets up JSON and MessagePack protocols.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration manager.</param>
    /// <returns>The service collection for chaining.</returns>
    internal static IServiceCollection AddSignalRInfrastructure(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        services.AddOptionsWithValidateOnStart<SignalROptions>()
            .BindConfiguration(SignalROptions.Section)
            .ValidateDataAnnotations();

        var signalROptions = configuration
            .GetRequiredSection(SignalROptions.Section)
            .Get<SignalROptions>()!;

        IConnectionMultiplexer redisMultiplexer =
            ConnectionMultiplexer.Connect(configuration.GetConnectionString(nameof(ConnectionStringsOptions.Redis))!);
        services.AddSingleton(redisMultiplexer);

        services.AddSignalR(options =>
            {
                options.EnableDetailedErrors = signalROptions.EnableDetailedErrors;
                options.ClientTimeoutInterval = TimeSpan.FromSeconds(signalROptions.ClientTimeoutSeconds);
                options.KeepAliveInterval = TimeSpan.FromSeconds(signalROptions.KeepAliveSeconds);
            })
            .AddJsonProtocol()
            .AddMessagePackProtocol(options =>
            {
                options.SerializerOptions = MessagePackSerializerOptions.Standard
                    .WithResolver(ContractlessStandardResolver.Instance)
                    .WithSecurity(MessagePackSecurity.UntrustedData);
            })
            .AddHubInstrumentation()
            .AddStackExchangeRedis(options =>
            {
                options.ConnectionFactory = _ => Task.FromResult(redisMultiplexer);
                options.Configuration.ChannelPrefix = RedisChannel.Literal("signalr.dotnetatlas");
            });

        return services;
    }
}
