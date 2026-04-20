using KafkaFlow;
using KafkaFlow.Configuration;
using Microsoft.Extensions.Logging;

namespace Platform.KafkaFlow.ProducerHeaders;

/// <summary>
/// Consumer-side DI extensions for the correlation-id binding middleware (ADR-0008).
/// </summary>
public static class KafkaFlowConsumerHeadersDependencyInjection
{
    /// <param name="builder">The consumer middleware configuration builder.</param>
    extension(IConsumerMiddlewareConfigurationBuilder builder)
    {
        /// <summary>
        /// Adds the <see cref="ConsumerCorrelationIdMiddleware"/> to the consumer pipeline.
        /// </summary>
        /// <returns>The builder for chaining.</returns>
        /// <remarks>
        /// <para>
        /// Place this middleware <em>first</em> in the consumer pipeline (immediately after the
        /// deserializer) so that retries, dead-letter produces, inbox dedup, and the typed handler
        /// all execute inside the correlation-id <see cref="System.Diagnostics.Activity"/> and
        /// Serilog <c>LogContext</c> scope.
        /// </para>
        /// <para>
        /// Example usage:
        /// <code>
        /// .AddConsumer(consumer =&gt; consumer
        ///     .AddMiddlewares(m =&gt; m
        ///         .AddSchemaRegistryAvroDeserializer()
        ///         .AddCorrelationIdConsumerMiddleware()
        ///         .AddDeadLetter()
        ///         .RetryForever(...)
        ///         .AddInbox(...)
        ///         .AddTypedHandlers(...)))
        /// </code>
        /// </para>
        /// </remarks>
        public IConsumerMiddlewareConfigurationBuilder AddCorrelationIdConsumerMiddleware()
        {
            return builder.Add(resolver =>
                new ConsumerCorrelationIdMiddleware(
                    resolver.Resolve<ILogger<ConsumerCorrelationIdMiddleware>>()));
        }
    }
}
