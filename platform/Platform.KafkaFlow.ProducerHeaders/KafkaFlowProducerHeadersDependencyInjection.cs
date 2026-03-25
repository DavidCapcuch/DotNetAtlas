using KafkaFlow.Configuration;

namespace Platform.KafkaFlow.ProducerHeaders;

/// <summary>
/// Extension methods for adding producer headers middleware to KafkaFlow producers.
/// </summary>
public static class KafkaFlowProducerHeadersDependencyInjection
{
    /// <param name="builder">The producer middleware configuration builder.</param>
    extension(IProducerMiddlewareConfigurationBuilder builder)
    {
        /// <summary>
        /// Adds the producer headers middleware to the producer pipeline.
        /// This middleware automatically adds MessageId and Origin headers to all outgoing messages.
        /// </summary>
        /// <param name="origin">The origin identifier to include in the Origin header.</param>
        /// <returns>The builder for chaining.</returns>
        /// <remarks>
        /// <para>
        /// This middleware should be added at the beginning of the producer middleware pipeline
        /// (before serializers) so that headers are available for all downstream middlewares.
        /// </para>
        /// <para>
        /// Example usage:
        /// <code>
        /// .AddProducer&lt;MyProducer&gt;(producer =&gt;
        ///     producer
        ///         .AddMiddlewares(m =&gt; m
        ///             .AddProducerHeaders("MyService")
        ///             .AddSchemaRegistryAvroSerializer(options)))
        /// </code>
        /// </para>
        /// </remarks>
        public IProducerMiddlewareConfigurationBuilder AddProducerHeaders(string origin)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(origin);

            var options = new ProducerHeadersOptions
            {
                Origin = origin
            };

            return builder.Add(resolver =>
                new ProducerHeadersMiddleware(
                    options));
        }

        /// <summary>
        /// Adds the producer headers middleware to the producer pipeline using pre-configured options.
        /// This middleware automatically adds MessageId and Origin headers to all outgoing messages.
        /// </summary>
        /// <param name="options">The pre-configured options containing the origin identifier.</param>
        /// <returns>The builder for chaining.</returns>
        public IProducerMiddlewareConfigurationBuilder AddProducerHeaders(ProducerHeadersOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentException.ThrowIfNullOrWhiteSpace(options.Origin);

            return builder.Add(resolver =>
                new ProducerHeadersMiddleware(
                    options));
        }
    }
}
