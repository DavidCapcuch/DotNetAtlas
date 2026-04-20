using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Platform.ServiceDefaults.CorrelationId;
using Serilog;
using Serilog.Context;
using Serilog.Core;
using Serilog.Events;

namespace Platform.ServiceDefaults.UnitTests.CorrelationId;

public class CorrelationIdMiddlewareTests
{
    private static readonly IOptions<CorrelationIdOptions> DefaultOptions = Options.Create(new CorrelationIdOptions());

    [Fact]
    public async Task InvokeAsync_WhenInboundHeaderIsValidUuidV7_PreservesValueAndEchoesOnResponse()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7().ToString();
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdContextKeys.HttpHeaderName] = correlationId;
        var middleware = new CorrelationIdMiddleware(
            NoOpNext,
            DefaultOptions,
            NullLogger<CorrelationIdMiddleware>.Instance);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        using (new AssertionScope())
        {
            context.Items[CorrelationIdContextKeys.HttpContextItemKey].Should().Be(correlationId);
            context.Response.Headers[CorrelationIdContextKeys.HttpHeaderName].ToString().Should().Be(correlationId);
        }
    }

    [Fact]
    public async Task InvokeAsync_WhenInboundHeaderIsMissing_GeneratesUuidV7AndEchoesOnResponse()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var middleware = new CorrelationIdMiddleware(
            NoOpNext,
            DefaultOptions,
            NullLogger<CorrelationIdMiddleware>.Instance);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        var generated = context.Response.Headers[CorrelationIdContextKeys.HttpHeaderName].ToString();
        using (new AssertionScope())
        {
            Guid.TryParse(generated, out var parsed).Should().BeTrue();
            IsUuidV7(parsed).Should().BeTrue();
            context.Items[CorrelationIdContextKeys.HttpContextItemKey].Should().Be(generated);
        }
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("12345")]
    [InlineData("")]
    public async Task InvokeAsync_WhenInboundHeaderIsMalformed_GeneratesReplacementUuidV7(string inbound)
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdContextKeys.HttpHeaderName] = inbound;
        var middleware = new CorrelationIdMiddleware(
            NoOpNext,
            DefaultOptions,
            NullLogger<CorrelationIdMiddleware>.Instance);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        var resolved = context.Response.Headers[CorrelationIdContextKeys.HttpHeaderName].ToString();
        using (new AssertionScope())
        {
            resolved.Should().NotBeNullOrWhiteSpace();
            resolved.Should().NotBe(inbound);
            Guid.TryParse(resolved, out var parsed).Should().BeTrue();
            IsUuidV7(parsed).Should().BeTrue();
        }
    }

    [Fact]
    public async Task InvokeAsync_WhenInboundHeaderIsUuidV4_ReplacesWithGeneratedV7()
    {
        // Arrange
        var v4 = Guid.NewGuid().ToString();
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdContextKeys.HttpHeaderName] = v4;
        var middleware = new CorrelationIdMiddleware(
            NoOpNext,
            DefaultOptions,
            NullLogger<CorrelationIdMiddleware>.Instance);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        var resolved = context.Response.Headers[CorrelationIdContextKeys.HttpHeaderName].ToString();
        using (new AssertionScope())
        {
            resolved.Should().NotBe(v4);
            Guid.TryParse(resolved, out var parsed).Should().BeTrue();
            IsUuidV7(parsed).Should().BeTrue();
        }
    }

    [Fact]
    public async Task InvokeAsync_WhenActivityIsPresent_SetsCorrelationTag()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7().ToString();
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdContextKeys.HttpHeaderName] = correlationId;
        using var source = new ActivitySource("Platform.ServiceDefaults.UnitTests");
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);
        using var activity = source.StartActivity("test")!;

        var middleware = new CorrelationIdMiddleware(
            _ => Task.CompletedTask,
            DefaultOptions,
            NullLogger<CorrelationIdMiddleware>.Instance);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        activity.GetTagItem(CorrelationIdContextKeys.ActivityTagName).Should().Be(correlationId);
    }

    [Fact]
    public async Task InvokeAsync_WhenProcessing_PushesSerilogLogContextPropertyDuringNextInvocation()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7().ToString();
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdContextKeys.HttpHeaderName] = correlationId;

        var sink = new CapturingSink();
        var logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(sink)
            .CreateLogger();

        RequestDelegate next = _ =>
        {
            logger.Information("in-scope");
            return Task.CompletedTask;
        };
        var middleware = new CorrelationIdMiddleware(
            next,
            DefaultOptions,
            NullLogger<CorrelationIdMiddleware>.Instance);

        // Act
        using (LogContext.Push()) // fresh context
        {
            await middleware.InvokeAsync(context);
        }

        // Assert
        sink.Events.Should().ContainSingle()
            .Which.Properties.Should().ContainKey(CorrelationIdContextKeys.SerilogPropertyName)
            .WhoseValue.ToString().Should().Contain(correlationId);
    }

    [Fact]
    public async Task InvokeAsync_WhenGenerateWhenMissingIsFalseAndHeaderMissing_SkipsPropagation()
    {
        // Arrange
        var options = Options.Create(new CorrelationIdOptions { GenerateWhenMissing = false });
        var context = new DefaultHttpContext();
        var middleware = new CorrelationIdMiddleware(
            NoOpNext,
            options,
            NullLogger<CorrelationIdMiddleware>.Instance);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        using (new AssertionScope())
        {
            context.Items.Should().NotContainKey(CorrelationIdContextKeys.HttpContextItemKey);
            context.Response.Headers.Should().NotContainKey(CorrelationIdContextKeys.HttpHeaderName);
        }
    }

    [Fact]
    public async Task InvokeAsync_WhenGenerateWhenMissingIsFalseAndHeaderIsMalformed_SkipsPropagation()
    {
        // Arrange — internal service behind an edge gateway: must NOT silently rewrite a bad inbound id
        // when the operator opted out of generation. The edge is responsible for the id; we only pass it through.
        var options = Options.Create(new CorrelationIdOptions { GenerateWhenMissing = false });
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdContextKeys.HttpHeaderName] = "not-a-guid";
        var middleware = new CorrelationIdMiddleware(
            NoOpNext,
            options,
            NullLogger<CorrelationIdMiddleware>.Instance);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        using (new AssertionScope())
        {
            context.Items.Should().NotContainKey(CorrelationIdContextKeys.HttpContextItemKey);
            context.Response.Headers.Should().NotContainKey(CorrelationIdContextKeys.HttpHeaderName);
        }
    }

    private static Task NoOpNext(HttpContext context) => Task.CompletedTask;

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
