using KafkaFlow;
using NSubstitute;

namespace Notifications.IntegrationTests.Common;

/// <summary>
/// Minimal <see cref="IMessageContext"/> stub for invoking Kafka handlers directly in tests
/// without standing up a KafkaFlow middleware stack.
/// </summary>
internal static class TestKafkaMessageContext
{
    public static IMessageContext Create(CancellationToken ct = default)
    {
        var ctx = Substitute.For<IMessageContext>();
        var consumerCtx = Substitute.For<IConsumerContext>();
        consumerCtx.WorkerStopped.Returns(ct);
        ctx.ConsumerContext.Returns(consumerCtx);
        return ctx;
    }
}
