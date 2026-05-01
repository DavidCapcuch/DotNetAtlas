using System.Text;
using KafkaFlow;
using NSubstitute;
using Platform.Messaging.Abstractions;

namespace Ordering.IntegrationTests.Common;

/// <summary>
/// Builds a minimal NSubstitute-backed <see cref="IMessageContext"/> good
/// enough for Ordering's M7 saga-command Kafka handler tests. Stubs the
/// bits the handlers actually read: <c>Headers</c> (so
/// <c>ExtractMessageId</c> / <c>ExtractOrigin</c> in
/// <c>SagaCommandHandlerBase</c> work) and
/// <c>ConsumerContext.WorkerStopped</c> (the cancellation token threaded
/// through every async call). Avoids standing up Testcontainers Kafka +
/// Schema Registry — matches the precedent set in M4 / M5 by
/// <c>test/Ordering.IntegrationTests/Common/IntegrationTestFixture.cs</c>
/// (no broker container; handlers invoked directly).
/// </summary>
internal static class FakeKafkaMessageContext
{
    /// <summary>
    /// Default origin string used when callers don't override. Mirrors the
    /// Checkout saga's producer origin per ADR-0008.
    /// </summary>
    public const string DefaultOrigin = "checkout-saga";

    /// <summary>
    /// Create a stubbed <see cref="IMessageContext"/>.
    /// </summary>
    /// <param name="messageId">
    /// Message id the inbox middleware would read. Defaults to a fresh
    /// GUIDv7 because the platform's <c>InboxMiddleware</c> requires a
    /// non-null header — even though M7 tests bypass the middleware
    /// (handlers are invoked directly), keeping the header populated
    /// guards against future refactors that wire the middleware in.
    /// </param>
    /// <param name="origin">Producer origin string (Kafka <c>origin</c> header).</param>
    /// <param name="correlationId">
    /// Optional correlation id (Kafka <c>correlation-id</c> header). Most
    /// Ordering tests source correlation off the Avro payload, not the
    /// header, so callers usually leave this null;
    /// <see cref="Ordering.IntegrationTests.Messaging.Kafka.CorrelationIdPropagationTests"/>
    /// pins the header explicitly.
    /// </param>
    /// <param name="cancellationToken">
    /// Token returned by <c>ConsumerContext.WorkerStopped</c>. Tests pass
    /// <c>TestContext.Current.CancellationToken</c> for cooperative xUnit
    /// cancellation.
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
        consumerContext.Topic.Returns("test-topic");
        consumerContext.Partition.Returns(0);
        consumerContext.Offset.Returns(0);

        var context = Substitute.For<IMessageContext>();
        context.Headers.Returns(headers);
        context.ConsumerContext.Returns(consumerContext);

        return context;
    }
}
