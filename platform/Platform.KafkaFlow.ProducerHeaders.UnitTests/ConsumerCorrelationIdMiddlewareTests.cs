using System.Diagnostics;
using System.Text;
using KafkaFlow;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Platform.KafkaFlow.ProducerHeaders;
using Platform.Messaging.Abstractions;
using Platform.ServiceDefaults.CorrelationId;
using Serilog;
using Serilog.Context;
using Serilog.Core;
using Serilog.Events;

namespace Platform.KafkaFlow.ProducerHeaders.UnitTests;

public class ConsumerCorrelationIdMiddlewareTests
{
    [Fact]
    public async Task Invoke_WhenValidUuidV7Header_PushesActivityTagAndLogContext()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7().ToString();
        var (context, _) = BuildContextWithHeader(MessageHeaderKeys.CorrelationId, correlationId);

        using var source = new ActivitySource("Platform.KafkaFlow.ProducerHeaders.UnitTests");
        using var listener = CreateAllDataListener();
        ActivitySource.AddActivityListener(listener);
        using var activity = source.StartActivity("consume")!;

        var sink = new CapturingSink();
        var logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(sink)
            .CreateLogger();

        string? tagInsideNext = null;
        MiddlewareDelegate next = _ =>
        {
            tagInsideNext = activity.GetTagItem(CorrelationIdContextKeys.ActivityTagName) as string;
            logger.Information("in-scope");
            return Task.CompletedTask;
        };
        var middleware = new ConsumerCorrelationIdMiddleware(
            NullLogger<ConsumerCorrelationIdMiddleware>.Instance);

        // Act
        using (LogContext.Push())
        {
            await middleware.Invoke(context, next);
        }

        // Assert
        using (new AssertionScope())
        {
            tagInsideNext.Should().Be(correlationId);
            sink.Events.Should().ContainSingle()
                .Which.Properties.Should().ContainKey(CorrelationIdContextKeys.SerilogPropertyName)
                .WhoseValue.ToString().Should().Contain(correlationId);
        }
    }

    [Fact]
    public async Task Invoke_WhenHeaderMissing_GeneratesUuidV7()
    {
        // Arrange
        var (context, _) = BuildContextWithHeaders();
        using var source = new ActivitySource("Platform.KafkaFlow.ProducerHeaders.UnitTests");
        using var listener = CreateAllDataListener();
        ActivitySource.AddActivityListener(listener);
        using var activity = source.StartActivity("consume")!;

        string? tagInsideNext = null;
        MiddlewareDelegate next = _ =>
        {
            tagInsideNext = activity.GetTagItem(CorrelationIdContextKeys.ActivityTagName) as string;
            return Task.CompletedTask;
        };
        var middleware = new ConsumerCorrelationIdMiddleware(
            NullLogger<ConsumerCorrelationIdMiddleware>.Instance);

        // Act
        await middleware.Invoke(context, next);

        // Assert
        using (new AssertionScope())
        {
            tagInsideNext.Should().NotBeNullOrEmpty();
            Guid.TryParse(tagInsideNext, out var parsed).Should().BeTrue();
            IsUuidV7(parsed).Should().BeTrue();
        }
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("12345")]
    public async Task Invoke_WhenHeaderMalformed_GeneratesUuidV7(string inbound)
    {
        // Arrange — malformed inbound; we still push a freshly-generated v7 so downstream logs are populated.
        var (context, _) = BuildContextWithHeader(MessageHeaderKeys.CorrelationId, inbound);
        using var source = new ActivitySource("Platform.KafkaFlow.ProducerHeaders.UnitTests");
        using var listener = CreateAllDataListener();
        ActivitySource.AddActivityListener(listener);
        using var activity = source.StartActivity("consume")!;

        string? tagInsideNext = null;
        MiddlewareDelegate next = _ =>
        {
            tagInsideNext = activity.GetTagItem(CorrelationIdContextKeys.ActivityTagName) as string;
            return Task.CompletedTask;
        };
        var middleware = new ConsumerCorrelationIdMiddleware(
            NullLogger<ConsumerCorrelationIdMiddleware>.Instance);

        // Act
        await middleware.Invoke(context, next);

        // Assert
        using (new AssertionScope())
        {
            tagInsideNext.Should().NotBe(inbound);
            Guid.TryParse(tagInsideNext, out var parsed).Should().BeTrue();
            IsUuidV7(parsed).Should().BeTrue();
        }
    }

    [Fact]
    public async Task Invoke_WhenHeaderIsUuidV4_GeneratesReplacement()
    {
        // Arrange — valid GUID but not v7; must be replaced (ADR-0008 mandates UUID v7).
        var v4 = Guid.NewGuid().ToString();
        var (context, _) = BuildContextWithHeader(MessageHeaderKeys.CorrelationId, v4);
        using var source = new ActivitySource("Platform.KafkaFlow.ProducerHeaders.UnitTests");
        using var listener = CreateAllDataListener();
        ActivitySource.AddActivityListener(listener);
        using var activity = source.StartActivity("consume")!;

        string? tagInsideNext = null;
        MiddlewareDelegate next = _ =>
        {
            tagInsideNext = activity.GetTagItem(CorrelationIdContextKeys.ActivityTagName) as string;
            return Task.CompletedTask;
        };
        var middleware = new ConsumerCorrelationIdMiddleware(
            NullLogger<ConsumerCorrelationIdMiddleware>.Instance);

        // Act
        await middleware.Invoke(context, next);

        // Assert
        using (new AssertionScope())
        {
            tagInsideNext.Should().NotBe(v4);
            Guid.TryParse(tagInsideNext, out var parsed).Should().BeTrue();
            IsUuidV7(parsed).Should().BeTrue();
        }
    }

    [Fact]
    public async Task Invoke_LogContextScope_DisposesAfterNextReturns()
    {
        // Arrange — assert the scoped property is NOT visible after next completes.
        var correlationId = Guid.CreateVersion7().ToString();
        var (context, _) = BuildContextWithHeader(MessageHeaderKeys.CorrelationId, correlationId);

        var sink = new CapturingSink();
        var logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(sink)
            .CreateLogger();

        MiddlewareDelegate next = _ =>
        {
            logger.Information("in-scope");
            return Task.CompletedTask;
        };
        var middleware = new ConsumerCorrelationIdMiddleware(
            NullLogger<ConsumerCorrelationIdMiddleware>.Instance);

        // Act
        using (LogContext.Push())
        {
            await middleware.Invoke(context, next);
            logger.Information("after-middleware");
        }

        // Assert
        using (new AssertionScope())
        {
            sink.Events.Should().HaveCount(2);
            sink.Events[0].Properties.Should()
                .ContainKey(CorrelationIdContextKeys.SerilogPropertyName);
            sink.Events[1].Properties.Should()
                .NotContainKey(CorrelationIdContextKeys.SerilogPropertyName);
        }
    }

    [Fact]
    public async Task Invoke_WhenNextThrows_LogContextScopeStillDisposes()
    {
        // Arrange — using-block must dispose the LogContext scope when next throws.
        var correlationId = Guid.CreateVersion7().ToString();
        var (context, _) = BuildContextWithHeader(MessageHeaderKeys.CorrelationId, correlationId);

        var sink = new CapturingSink();
        var logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(sink)
            .CreateLogger();

        MiddlewareDelegate next = _ => throw new InvalidOperationException("boom");
        var middleware = new ConsumerCorrelationIdMiddleware(
            NullLogger<ConsumerCorrelationIdMiddleware>.Instance);

        // Act
        Func<Task> act = async () =>
        {
            using (LogContext.Push())
            {
                await middleware.Invoke(context, next);
            }
        };

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        logger.Information("after-throw");
        sink.Events.Should().ContainSingle()
            .Which.Properties.Should().NotContainKey(CorrelationIdContextKeys.SerilogPropertyName);
    }

    private static (IMessageContext Context, MessageHeaders Headers) BuildContextWithHeaders()
    {
        var headers = new MessageHeaders();
        var context = Substitute.For<IMessageContext>();
        context.Headers.Returns(headers);
        return (context, headers);
    }

    private static (IMessageContext Context, MessageHeaders Headers) BuildContextWithHeader(string key, string value)
    {
        var (context, headers) = BuildContextWithHeaders();
        headers.Add(key, Encoding.UTF8.GetBytes(value));
        return (context, headers);
    }

    private static ActivityListener CreateAllDataListener() =>
        new()
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };

    private static bool IsUuidV7(Guid guid)
    {
        Span<byte> bytes = stackalloc byte[16];
        guid.TryWriteBytes(bytes, bigEndian: true, out _);
        return (bytes[6] >> 4) == 0x7;
    }

    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
