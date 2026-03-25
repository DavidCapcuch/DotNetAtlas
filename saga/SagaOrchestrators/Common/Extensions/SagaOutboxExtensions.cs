using Avro.Specific;
using MassTransit;
using Platform.ReliableMessaging.Outbox.EFCore;
using SagaOrchestrators.Persistence.Database;

namespace SagaOrchestrators.Common.Extensions;

/// <summary>
/// Extension methods for adding outbox messages from within MassTransit saga state machines.
/// </summary>
/// <remarks>
/// These extensions solve the scoped service resolution problem where:
/// - The saga state machine is a singleton
/// - The transactional outbox requires a scoped DbContext
///
/// By resolving the DbContext from the behavior context's service scope,
/// we get the same scoped DbContext instance that MassTransit uses for saga persistence,
/// ensuring outbox messages are saved in the same transaction as saga state changes.
/// </remarks>
public static class SagaOutboxExtensions
{
    /// <summary>
    /// Adds an integration event to the outbox using functions to compute the key and create the message.
    /// Provides a more fluent syntax for publishing outbox messages in the state machine chain.
    /// </summary>
    /// <typeparam name="TSaga">The saga state type.</typeparam>
    /// <typeparam name="TMessage">The message type being handled.</typeparam>
    /// <param name="binder">The EventActivityBinder from the state machine.</param>
    /// <param name="topicName">The Kafka topic where the message will be published.</param>
    /// <param name="keyFactory">Function to compute the Kafka key from the context.</param>
    /// <param name="messageFactory">Function to create the integration event from the context.</param>
    /// <returns>The EventActivityBinder for continued fluent chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the DbContext or IOutboxWriter cannot be resolved.</exception>
    /// <example>
    /// <code>
    /// .PublishToOutbox(
    ///     "finance.payments",
    ///     ctx => ctx.Saga.CorrelationId.ToString(),
    ///     ctx => new PaymentRequestedEvent { /* ... */ })
    /// </code>
    /// </example>
    public static EventActivityBinder<TSaga, TMessage> PublishToOutbox<TSaga, TMessage>(
        this EventActivityBinder<TSaga, TMessage> binder,
        string topicName,
        Func<BehaviorContext<TSaga, TMessage>, string?> keyFactory,
        Func<BehaviorContext<TSaga, TMessage>, ISpecificRecord> messageFactory)
        where TSaga : class, SagaStateMachineInstance
        where TMessage : class
    {
        return binder.Then(ctx =>
        {
            var kafkaKey = keyFactory(ctx);
            var integrationEvent = messageFactory(ctx);
            var (dbContext, outboxWriter) = GetOutboxDependencies(ctx);
            outboxWriter.AddOutboxMessage(dbContext, topicName, kafkaKey, integrationEvent);
        });
    }

    private static (SagaDbContext DbContext, IOutboxWriter OutboxWriter) GetOutboxDependencies<TSaga>(
        BehaviorContext<TSaga> context)
        where TSaga : class, SagaStateMachineInstance
    {
        // Get the service scope from the behavior context
        // MassTransit creates a scope for each message, so we get the scoped DbContext
        // See https://github.com/MassTransit/MassTransit/discussions/3365
        if (!context.TryGetPayload<IServiceScope>(out var serviceScope))
        {
            throw new InvalidOperationException(
                "Unable to resolve IServiceScope from the behavior context. " +
                "Ensure the saga is configured with an Entity Framework repository.");
        }

        var serviceProvider = serviceScope.ServiceProvider;
        var dbContext = serviceProvider.GetRequiredService<SagaDbContext>();
        var outboxWriter = serviceProvider.GetRequiredService<IOutboxWriter>();

        return (dbContext, outboxWriter);
    }
}
