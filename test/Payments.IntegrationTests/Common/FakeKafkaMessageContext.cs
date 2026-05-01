using System.Text;
using KafkaFlow;
using NSubstitute;
using Platform.Messaging.Abstractions;

namespace Payments.IntegrationTests.Common;

/// <summary>
/// Builds a minimal NSubstitute-backed <see cref="IMessageContext"/> good enough for Payments'
/// M5 saga-command Kafka handler tests. Stubs the bits the handlers actually read:
/// <c>Headers</c> (so <c>ExtractMessageId</c> / <c>ExtractOrigin</c> in
/// <see cref="Payments.Infrastructure.Messaging.Kafka.PaymentCommands.SagaCommandHandlerBase{T}"/>
/// work) and <c>ConsumerContext.WorkerStopped</c> (the cancellation token threaded through
/// every async call). Avoids standing up Testcontainers Kafka + Schema Registry — matches the
/// precedent set by Ordering's M7 fixture at
/// <c>test/Ordering.IntegrationTests/Common/FakeKafkaMessageContext.cs</c>.
/// </summary>
internal static class FakeKafkaMessageContext
{
    /// <summary>
    /// Default origin string used when callers don't override.
    /// </summary>
    public const string DefaultOrigin = "payment-processing-saga";

    /// <summary>
    /// Create a stubbed <see cref="IMessageContext"/>.
    /// </summary>
    /// <param name="messageId">
    /// Message id the inbox middleware would read. Defaults to a fresh GUIDv7 — even though M5
    /// tests bypass the middleware (handlers are invoked directly), keeping the header
    /// populated guards against future refactors that wire the middleware in.
    /// </param>
    /// <param name="origin">Producer origin string (Kafka <c>origin</c> header).</param>
    /// <param name="correlationId">
    /// Optional correlation id (Kafka <c>correlation-id</c> header). Most Payments tests source
    /// correlation off the Avro payload, not the header.
    /// </param>
    /// <param name="cancellationToken">
    /// Token returned by <c>ConsumerContext.WorkerStopped</c>. Tests pass
    /// <c>TestContext.Current.CancellationToken</c> for cooperative xUnit cancellation.
    /// </param>
    public static IMessageContext Create(
        Guid? messageId = null,
        string origin = DefaultOrigin,
        Guid? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        var headers = new MessageHeaders
        {
            { MessageHeaderKeys.MessageId, Encoding.UTF8.GetBytes((messageId ?? Guid.CreateVersion7()).ToString()) },
            { MessageHeaderKeys.Origin, Encoding.UTF8.GetBytes(origin) },
        };

        if (correlationId is not null)
        {
            headers.Add(
                MessageHeaderKeys.CorrelationId,
                Encoding.UTF8.GetBytes(correlationId.Value.ToString()));
        }

        var consumerContext = Substitute.For<IConsumerContext>();
        consumerContext.WorkerStopped.Returns(cancellationToken);
        consumerContext.Topic.Returns("payments.commands");
        consumerContext.Partition.Returns(0);
        consumerContext.Offset.Returns(0);

        var context = Substitute.For<IMessageContext>();
        context.Headers.Returns(headers);
        context.ConsumerContext.Returns(consumerContext);

        return context;
    }
}
