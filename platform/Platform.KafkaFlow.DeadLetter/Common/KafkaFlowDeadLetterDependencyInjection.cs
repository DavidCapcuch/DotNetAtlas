using KafkaFlow;
using KafkaFlow.Configuration;
using Microsoft.Extensions.Logging;

namespace Platform.KafkaFlow.DeadLetter.Common;

/// <summary>
/// Extension methods for adding Dead Letter Topic middleware to KafkaFlow consumers.
/// </summary>
public static class KafkaFlowDeadLetterDependencyInjection
{
    /// <summary>
    /// Adds a Dead Letter Topic producer to the cluster.
    /// This must be called before using <see cref="AddDeadLetter"/> on consumers.
    /// </summary>
    /// <param name="builder">The cluster configuration builder.</param>
    /// <param name="topicSuffix">Suffix appended to original topic name to create DLT topic (e.g., ".DLT").
    /// A leading "." will be added if not present.</param>
    /// <param name="configure">Optional producer configuration (compression, acks, serializers, etc.).</param>
    /// <returns>The builder for chaining.</returns>
    public static IClusterConfigurationBuilder AddDltProducer(
        this IClusterConfigurationBuilder builder,
        string topicSuffix,
        Action<IProducerConfigurationBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topicSuffix);

        var normalizedSuffix = topicSuffix.StartsWith('.') ? topicSuffix : $".{topicSuffix}";

        builder.DependencyConfigurator.AddSingleton(new DltTopicSuffix(normalizedSuffix));

        return builder.AddProducer<DeadLetterMiddleware>(producer =>
        {
            configure?.Invoke(producer);
        });
    }

    /// <summary>
    /// Adds Dead Letter middleware to the consumer pipeline.
    /// This middleware catches unhandled exceptions and sends them to a DLT topic.
    /// Requires <see cref="AddDltProducer"/> to be called on the cluster first.
    /// </summary>
    /// <param name="builder">The middleware configuration builder.</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// Place this middleware at the outermost position in the pipeline (first in the chain)
    /// so it can catch all unhandled exceptions from inner middlewares.
    /// </remarks>
    public static IConsumerMiddlewareConfigurationBuilder AddDeadLetter(
        this IConsumerMiddlewareConfigurationBuilder builder)
    {
        return builder.Add<DeadLetterMiddleware>(resolver =>
            new DeadLetterMiddleware(
                resolver.Resolve<IMessageProducer<DeadLetterMiddleware>>(),
                resolver.Resolve<DltTopicSuffix>().Value,
                resolver.Resolve<ILogger<DeadLetterMiddleware>>()));
    }
}
