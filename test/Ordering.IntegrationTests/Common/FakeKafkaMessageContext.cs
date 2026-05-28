using System.Text;
using KafkaFlow;
using NSubstitute;
using Platform.Messaging.Abstractions;

namespace Ordering.IntegrationTests.Common;

/// <summary>
/// Builds a minimal NSubstitute-backed <see cref="IMessageContext"/> good
/// enough for Ordering's saga-command Kafka handler tests. Stubs the
/// bits the handlers actually read: <c>Headers</c> (so
/// <c>ExtractMessageId</c> / <c>ExtractOrigin</c> in
/// <c>SagaCommandHandlerBase</c> work) and
/// <c>ConsumerContext.WorkerStopped</c> (the cancellation token threaded
/// through every async call). Avoids standing up Testcontainers Kafka +
/// Schema Registry — matches the precedent set by
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
    /// non-null header — even though tests bypass the middleware
    /// (handlers are invoked directly), keeping the header populated
    /// guards against future refactors that wire the middleware in.
    /// </param>
    /// <param name="origin">Producer origin string (Kafka <c>origin</c> header).</param>
    /// <param name="correlationId">
    /// Correlation id (Kafka <c>correlation-id</c> header). Per ADR-0008 the header is the
    /// authoritative source for handlers; if the test asserts on a specific correlation id
    /// flowing through, callers MUST pass the same value here as the Avro payload sets.
    /// When <c>null</c>, a fresh UUID v7 is generated and pushed onto the header so handlers
    /// never see a missing header.
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
            // ADR-0008 — always set the correlation-id header. Production
            // ConsumerCorrelationIdMiddleware generates a replacement when the inbound header
            // is missing; the test fixture mirrors that shape.
            {
                MessageHeaderKeys.CorrelationId,
                Encoding.UTF8.GetBytes((correlationId ?? Guid.CreateVersion7()).ToString())
            },
        };

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
