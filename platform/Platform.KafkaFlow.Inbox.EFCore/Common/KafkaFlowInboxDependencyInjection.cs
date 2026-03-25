using EntityFramework.Exceptions.Common;
using KafkaFlow.Configuration;
using Microsoft.Extensions.Logging;
using Platform.ReliableMessaging.Inbox.EFCore;
using Platform.ReliableMessaging.Inbox.EFCore.Common;

namespace Platform.KafkaFlow.Inbox.EFCore.Common;

/// <summary>
/// Extension methods for adding inbox middleware to KafkaFlow consumers.
/// </summary>
public static class KafkaFlowInboxDependencyInjection
{
    /// <summary>
    /// Adds inbox middleware to the consumer pipeline for message deduplication,
    /// applying only to the specified message types.
    /// </summary>
    /// <param name="builder">The consumer middleware configuration builder.</param>
    /// <param name="messageTypes">
    /// The message types to apply inbox deduplication to.
    /// Messages of other types will pass through without deduplication for performance.
    /// </param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>Important:</b> The DbContext used with inbox middleware must be configured with
    /// <c>UseExceptionProcessor()</c> from the EntityFramework.Exceptions library.
    /// This converts database-specific constraint violations to a common <see cref="UniqueConstraintException"/>
    /// that the middleware can catch during concurrent message processing.
    /// </para>
    /// <para>
    /// Place this middleware AFTER Retry/DLT middleware so that transient failures
    /// are retried before messages are marked as processed.
    /// </para>
    /// <para>
    /// Requires <see cref="IInboxDbContext"/> to be registered in DI (scoped lifetime).
    /// Use <see cref="InboxDependencyInjection.AddInbox{TContext}"/> during service registration.
    /// </para>
    /// <example>
    /// <code>
    /// // During service registration
    /// services.AddDbContextPool&lt;MyDbContext&gt;(options => options
    ///     .UseSqlServer(connectionString)
    ///     .UseExceptionProcessor());
    /// services.AddInbox&lt;MyDbContext&gt;();
    ///
    /// // During consumer configuration
    /// .AddConsumer(consumer => consumer
    ///     .Topic("my-topic")
    ///     .AddMiddlewares(middlewares => middlewares
    ///         .RetryForever(...)
    ///         .AddInbox(typeof(OrderCreatedEvent), typeof(OrderUpdatedEvent))
    ///         .AddTypedHandlers(...)))
    /// </code>
    /// </example>
    /// </remarks>
    public static IConsumerMiddlewareConfigurationBuilder AddInbox(
        this IConsumerMiddlewareConfigurationBuilder builder,
        params Type[] messageTypes)
    {
        ArgumentNullException.ThrowIfNull(messageTypes);

        return builder.Add(resolver =>
            new InboxMiddleware(
                (IInboxDbContext)resolver.Resolve(typeof(IInboxDbContext)),
                (TimeProvider)resolver.Resolve(typeof(TimeProvider)),
                (ILogger<InboxMiddleware>)resolver.Resolve(typeof(ILogger<InboxMiddleware>)),
                messageTypes));
    }
}
