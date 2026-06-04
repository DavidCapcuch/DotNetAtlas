using System.Text;
using KafkaFlow;
using NSubstitute;
using Platform.Messaging.Abstractions;

namespace Platform.Test.Framework.Kafka;

/// <summary>
/// Builds a minimal NSubstitute-backed <see cref="IMessageContext"/> good
/// enough for any BC's saga-command or domain-event Kafka handler tests.
/// Stubs the bits handlers actually read:
/// <list type="bullet">
///   <item><c>Headers</c> — <c>MessageId</c>, <c>Origin</c>, and (until ADR-0030 #310 retargets
///   the BC consumer reads onto wire fields) <c>CorrelationId</c>, so <c>ExtractMessageId</c> /
///   <c>ExtractOrigin</c> / <c>ExtractCorrelationId</c> never see a missing header in isolation.</item>
///   <item><c>ConsumerContext.WorkerStopped</c> — the cancellation token threaded through every
///   handler's async call chain.</item>
///   <item><c>ConsumerContext.Topic</c> / <c>Partition</c> / <c>Offset</c> — stubbed to
///   constant non-null values for any handler that reads them for logging.</item>
/// </list>
/// Avoids standing up Testcontainers Kafka + Schema Registry — handlers are
/// invoked directly via <c>Handle(IMessageContext, T)</c> from the BC's test
/// classes.
/// </summary>
/// <remarks>
/// Hoisted from three byte-identical BC-local copies (Inventory, Ordering,
/// Payments). Tests in this repo do not assert on the stubbed <c>Topic</c>
/// or <c>Origin</c> values; callers that need specific values pass them
/// explicitly via the optional parameters.
/// </remarks>
public static class FakeKafkaMessageContext
{
    /// <summary>
    /// Default origin string when callers don't override. Generic so the
    /// platform helper isn't tied to any BC's saga producer naming.
    /// </summary>
    public const string DefaultOrigin = "test-producer";

    /// <summary>
    /// Default topic string when callers don't override. Tests in this repo
    /// don't assert on the topic via <see cref="IConsumerContext.Topic"/>;
    /// override only if a handler under test logs / branches on the topic.
    /// </summary>
    public const string DefaultTopic = "test-topic";

    /// <summary>
    /// Creates a stubbed <see cref="IMessageContext"/>.
    /// </summary>
    /// <param name="messageId">
    /// Message id the inbox middleware would read. Defaults to a fresh GUIDv7 —
    /// even though tests bypass the middleware (handlers are invoked
    /// directly), keeping the header populated guards against future
    /// refactors that wire the middleware in.
    /// </param>
    /// <param name="origin">Producer origin string (Kafka <c>origin</c> header).</param>
    /// <param name="correlationId">
    /// Correlation id (Kafka <c>correlation-id</c> header). Per ADR-0008 the header is the
    /// authoritative source for handlers; if the test asserts on a specific correlation id
    /// flowing through to outbox / projection rows, callers MUST pass the same value here as
    /// the Avro payload sets so header == payload. When <c>null</c>, a fresh UUID v7 is
    /// generated and pushed onto the header so handlers never see a missing header.
    /// </param>
    /// <param name="topic">Topic returned by <see cref="IConsumerContext.Topic"/>.</param>
    /// <param name="cancellationToken">
    /// Token returned by <c>ConsumerContext.WorkerStopped</c>. Tests pass
    /// <c>TestContext.Current.CancellationToken</c> for cooperative xUnit
    /// cancellation. Placed last to satisfy CA1068.
    /// </param>
    public static IMessageContext Create(
        Guid? messageId = null,
        string origin = DefaultOrigin,
        Guid? correlationId = null,
        string topic = DefaultTopic,
        CancellationToken cancellationToken = default)
    {
        var headers = new MessageHeaders
        {
            { MessageHeaderKeys.MessageId, Encoding.UTF8.GetBytes((messageId ?? Guid.CreateVersion7()).ToString()) },
            { MessageHeaderKeys.Origin, Encoding.UTF8.GetBytes(origin) },
            // Always set the correlation-id header: the BC saga-command / projection consumers
            // still read it via ExtractCorrelationId() until ADR-0030 #310 retargets those reads
            // onto wire fields, so the fixture keeps it populated so handlers never branch on
            // header-absence in isolation.
            {
                MessageHeaderKeys.CorrelationId,
                Encoding.UTF8.GetBytes((correlationId ?? Guid.CreateVersion7()).ToString())
            },
        };

        var consumerContext = Substitute.For<IConsumerContext>();
        consumerContext.WorkerStopped.Returns(cancellationToken);
        consumerContext.Topic.Returns(topic);
        consumerContext.Partition.Returns(0);
        consumerContext.Offset.Returns(0);

        var context = Substitute.For<IMessageContext>();
        context.Headers.Returns(headers);
        context.ConsumerContext.Returns(consumerContext);

        return context;
    }
}
