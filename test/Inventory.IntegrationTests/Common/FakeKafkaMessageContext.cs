using System.Text;
using KafkaFlow;
using NSubstitute;
using Platform.Messaging.Abstractions;

namespace Inventory.IntegrationTests.Common;

/// <summary>
/// Builds a minimal NSubstitute-backed <see cref="IMessageContext"/> good
/// enough for Inventory's M5 Kafka handler tests. Stubs the bits the
/// handlers actually read: <c>Headers</c> (so
/// <c>ExtractMessageId</c>/<c>ExtractOrigin</c> work) and
/// <c>ConsumerContext.WorkerStopped</c> (the cancellation token the
/// handlers thread through every async call). Avoids standing up
/// Testcontainers Kafka + Schema Registry — matches Ordering's M5
/// precedent
/// (<c>test/Ordering.IntegrationTests/Common/IntegrationTestFixture.cs:19-20</c>).
/// </summary>
internal static class FakeKafkaMessageContext
{
    /// <summary>
    /// Default origin string used when callers don't override. Mirrors the
    /// saga / upstream-BC origin a real Kafka payload would carry.
    /// </summary>
    public const string DefaultOrigin = "checkout-saga";

    /// <summary>
    /// Create a stubbed <see cref="IMessageContext"/>.
    /// </summary>
    /// <param name="messageId">
    /// Message id the inbox middleware would read. Defaults to a fresh GUIDv7
    /// because the platform's <c>InboxMiddleware</c> requires a non-null
    /// header — even though M5 tests bypass the middleware (they call the
    /// typed handler directly), keeping the header populated guards against
    /// future refactors that wire the middleware in.
    /// </param>
    /// <param name="origin">Producer origin string (Kafka <c>origin</c> header).</param>
    /// <param name="correlationId">
    /// Correlation id (Kafka <c>correlation-id</c> header). Per ADR-0008 the header is the
    /// authoritative source for handlers; if the test asserts on a specific correlation id
    /// flowing through to outbox / projection rows, callers MUST pass the same value here as
    /// the Avro payload sets so header == payload. When <c>null</c>, a fresh UUID v7 is
    /// generated and pushed onto the header so handlers never see a missing header.
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
            // ADR-0008 — always set the correlation-id header. Production ConsumerCorrelationIdMiddleware
            // generates a replacement when the inbound header is missing; the test fixture mirrors that
            // shape so handlers never branch on header-absence in isolation.
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
