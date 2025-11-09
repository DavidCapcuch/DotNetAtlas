using Confluent.Kafka;
using DotNetAtlas.OutboxRelay.WorkerService.Observability.Metrics;
using DotNetAtlas.OutboxRelay.WorkerService.OutboxRelay;
using DotNetAtlas.OutboxRelay.WorkerService.OutboxRelay.Config;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.OutboxRelay.Benchmark;

/// <summary>
/// Fluent builder for creating OutboxMessageRelay instances with customizable configurations.
/// Allows configuration of both OutboxRelayOptions and KafkaProducerOptions through method chaining.
/// </summary>
public sealed class OutboxMessageRelayBuilder
{
    private readonly IServiceProvider _serviceProvider;
    private Action<OutboxRelayOptions>? _outboxRelayConfigAction;
    private Action<KafkaProducerOptions>? _kafkaProducerConfigAction;

    public OutboxMessageRelayBuilder(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Configures OutboxRelayOptions by applying the provided action to the base configuration.
    /// This allows overriding properties like BatchSize, DefaultTopicName, etc.
    /// </summary>
    /// <param name="configAction">Action to configure OutboxRelayOptions.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public OutboxMessageRelayBuilder WithOutboxRelayConfig(Action<OutboxRelayOptions> configAction)
    {
        _outboxRelayConfigAction = configAction;

        return this;
    }

    /// <summary>
    /// Configures KafkaProducerOptions by applying the provided action to the base configuration.
    /// This allows overriding properties like CompressionType, LingerMs, Acks, etc.
    /// </summary>
    /// <param name="configAction">Action to configure KafkaProducerOptions.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public OutboxMessageRelayBuilder WithKafkaProducerConfig(Action<KafkaProducerOptions> configAction)
    {
        _kafkaProducerConfigAction = configAction;

        return this;
    }

    /// <summary>
    /// Builds and returns an OutboxMessageRelay instance with the configured options.
    /// Merges base configurations from DI with any overrides specified through configuration methods.
    /// </summary>
    /// <returns>A new OutboxMessageRelay instance.</returns>
    public OutboxMessageRelay Build()
    {
        var dbContextFactory = _serviceProvider.GetRequiredService<IDbContextFactory<OutboxDbContext>>();
        var logger = _serviceProvider.GetRequiredService<ILogger<OutboxMessageRelay>>();
        var metrics = _serviceProvider.GetRequiredService<OutboxRelayMetrics>();
        var cache = _serviceProvider.GetRequiredService<IMemoryCache>();

        // Shallow clones are enough
        var kafkaProducerOptions =
            _serviceProvider.GetRequiredService<IOptions<KafkaProducerOptions>>().Value.ShallowClone();
        var outboxRelayOptions =
            _serviceProvider.GetRequiredService<IOptions<OutboxRelayOptions>>().Value.ShallowClone();

        _kafkaProducerConfigAction?.Invoke(kafkaProducerOptions);
        _outboxRelayConfigAction?.Invoke(outboxRelayOptions);

        var producer = new ProducerBuilder<string?, byte[]>(kafkaProducerOptions).Build();
        return new OutboxMessageRelay(
            dbContextFactory,
            producer,
            Options.Create(outboxRelayOptions),
            logger,
            metrics,
            cache);
    }
}
