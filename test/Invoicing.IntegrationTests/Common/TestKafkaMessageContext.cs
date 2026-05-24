using System.Text;
using KafkaFlow;
using NSubstitute;
using Platform.Messaging.Abstractions;

namespace Invoicing.IntegrationTests.Common;

/// <summary>
/// Minimal <see cref="IMessageContext"/> stub for invoking Kafka handlers directly in tests
/// without standing up a KafkaFlow middleware stack. Always populates the Kafka
/// <c>correlation-id</c> header per ADR-0008 so handlers' <c>ExtractCorrelationId()</c>
/// reads a real value rather than throwing on missing.
/// </summary>
internal static class TestKafkaMessageContext
{
    /// <param name="correlationId">
    /// Correlation id pushed into the <c>correlation-id</c> header. Per ADR-0008 handlers
    /// source from the header, not the Avro payload; tests that assert on a specific
    /// correlation id MUST pass the same value here as the Avro carries. When <c>null</c>,
    /// a fresh UUID v7 is generated.
    /// </param>
    public static IMessageContext Create(Guid? correlationId = null, CancellationToken ct = default)
    {
        var headers = new MessageHeaders
        {
            {
                MessageHeaderKeys.CorrelationId,
                Encoding.UTF8.GetBytes((correlationId ?? Guid.CreateVersion7()).ToString())
            },
        };

        var ctx = Substitute.For<IMessageContext>();
        ctx.Headers.Returns(headers);
        var consumerCtx = Substitute.For<IConsumerContext>();
        consumerCtx.WorkerStopped.Returns(ct);
        ctx.ConsumerContext.Returns(consumerCtx);
        return ctx;
    }
}
