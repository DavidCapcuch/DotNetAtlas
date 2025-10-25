using AspNetCore.SignalR.OpenTelemetry;
using DotNetAtlas.Application.WeatherAlerts.Common.Abstractions;
using DotNetAtlas.Application.WeatherForecast.Common.Abstractions;
using DotNetAtlas.Infrastructure.Common.Config;
using DotNetAtlas.Infrastructure.Messaging.Kafka.Config;
using DotNetAtlas.Infrastructure.Messaging.Kafka.WeatherForecastEvents;
using DotNetAtlas.Infrastructure.Messaging.SignalR;
using KafkaFlow;
using KafkaFlow.Configuration;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace DotNetAtlas.Infrastructure.Common;

/// <summary>
/// Dependency injection extensions for communication infrastructure.
/// Configures Kafka messaging and SignalR real-time communication.
/// </summary>
internal static class MessagingDependencyInjection
{
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

        services.AddOptionsWithValidateOnStart<KafkaForecastEventsProducerOptions>()
            .BindConfiguration(KafkaForecastEventsProducerOptions.Section)
            .ValidateDataAnnotations();

        var kafkaOptions = configuration
            .GetRequiredSection(KafkaOptions.Section)
            .Get<KafkaOptions>()!;

        var producerOptions = configuration
            .GetRequiredSection(KafkaForecastEventsProducerOptions.Section)
            .Get<KafkaForecastEventsProducerOptions>()!;

        services.AddKafka(kafka => kafka
            .AddCluster(cluster => cluster
                .WithBrokers(kafkaOptions.Brokers)
                .WithSchemaRegistry(config => config.Url = kafkaOptions.SchemaRegistry.Url)
                .AddProducer<KafkaForecastEventsProducer>(producer =>
                    producer
                        .WithProducerConfig(producerOptions)
                        .AddMiddlewares(m =>
                            m.AddSchemaRegistryAvroSerializer(kafkaOptions.AvroSerializer))
                ))
            .UseMicrosoftLog()
            .AddOpenTelemetryInstrumentation());

        services.AddSingleton<IForecastEventsProducer, KafkaForecastEventsProducer>();

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
        services.AddSingleton<IGroupManager, RedisSignalRGroupManager>();

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
