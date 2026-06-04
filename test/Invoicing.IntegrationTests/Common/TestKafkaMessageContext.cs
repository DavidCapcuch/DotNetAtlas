using KafkaFlow;
using NSubstitute;

namespace Invoicing.IntegrationTests.Common;

/// <summary>
/// Minimal <see cref="IMessageContext"/> stub for invoking Kafka handlers directly in tests
/// without standing up a KafkaFlow middleware stack.
/// </summary>
internal static class TestKafkaMessageContext
{
    public static IMessageContext Create(CancellationToken ct = default)
    {
        var ctx = Substitute.For<IMessageContext>();
        ctx.Headers.Returns(new MessageHeaders());
        var consumerCtx = Substitute.For<IConsumerContext>();
        consumerCtx.WorkerStopped.Returns(ct);
        ctx.ConsumerContext.Returns(consumerCtx);
        return ctx;
    }
}
