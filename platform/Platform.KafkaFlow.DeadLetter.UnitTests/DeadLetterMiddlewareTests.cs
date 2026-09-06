using Confluent.Kafka;
using KafkaFlow;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Platform.KafkaFlow.DeadLetter.UnitTests;

public sealed class DeadLetterMiddlewareTests
{
    private const string SourceTopic = "ordering.order-commands";
    private const string TopicSuffix = ".Ordering.DLT";

    /// <summary>
    /// The broker does not auto-create topics, so a dead-letter topic missing from the
    /// <c>kafka-create-topic</c> block makes the produce throw. KafkaFlow's worker commits the
    /// offset regardless, so the message is dropped — and the record must say so, carrying both the
    /// poison message's cause and the produce failure. A record claiming the message was routed
    /// sends the operator to an empty topic.
    /// </summary>
    [Fact]
    public async Task Invoke_WhenTheDeadLetterProduceFails_RecordsTheDropWithBothCauses()
    {
        var poison = new InvalidOperationException("handler blew up on a malformed payload");
        var produceFailure = new KafkaException(ErrorCode.UnknownTopicOrPart);

        var producer = Substitute.For<IMessageProducer<DeadLetterMiddleware>>();
        producer
            .ProduceAsync(
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<object>(),
                Arg.Any<IMessageHeaders>())
            .ThrowsAsync(produceFailure);

        var logger = new RecordingLogger<DeadLetterMiddleware>();
        var middleware = new DeadLetterMiddleware(producer, TopicSuffix, logger);

        var invoke = async () => await middleware.Invoke(
            MessageContext(),
            _ => throw poison);

        using var _ = new AssertionScope();
        (await invoke.Should().ThrowAsync<KafkaException>())
            .Which.Should().BeSameAs(produceFailure, "the produce failure is what leaves the middleware");

        var record = logger.Entries.Should().ContainSingle().Which;
        record.Message.Should().Contain("FAILED", "the record must not claim a routing that did not happen");
        record.Exception.Should().BeOfType<AggregateException>()
            .Which.InnerExceptions.Should().SatisfyRespectively(
                first => first.Should().BeSameAs(poison, "the poison cause must survive"),
                second => second.Should().BeSameAs(produceFailure, "so must the reason it could not be routed"));
    }

    [Fact]
    public async Task Invoke_WhenTheDeadLetterProduceSucceeds_SendsToTheSuffixedTopic()
    {
        var producer = Substitute.For<IMessageProducer<DeadLetterMiddleware>>();
        var middleware = new DeadLetterMiddleware(
            producer,
            TopicSuffix,
            new RecordingLogger<DeadLetterMiddleware>());

        await middleware.Invoke(
            MessageContext(),
            _ => throw new InvalidOperationException("handler blew up"));

        await producer.Received(1).ProduceAsync(
            SourceTopic + TopicSuffix,
            Arg.Any<object>(),
            Arg.Any<object>(),
            Arg.Any<IMessageHeaders>());
    }

    private static IMessageContext MessageContext()
    {
        var consumerContext = Substitute.For<IConsumerContext>();
        consumerContext.Topic.Returns(SourceTopic);
        consumerContext.Partition.Returns(0);
        consumerContext.Offset.Returns(42);

        var context = Substitute.For<IMessageContext>();
        context.ConsumerContext.Returns(consumerContext);
        context.Headers.Returns(new MessageHeaders());
        context.Message.Returns(new Message("order-1", new object()));
        return context;
    }

    /// <summary>
    /// A hand-rolled recorder rather than a substituted <see cref="ILogger{TCategoryName}"/>: the
    /// assertion is about which exception reached the log, and <c>ILogger.Log</c>'s generic state
    /// parameter makes that awkward to express against a mock.
    /// </summary>
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Exception, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, exception, formatter(state, exception)));
        }
    }
}
