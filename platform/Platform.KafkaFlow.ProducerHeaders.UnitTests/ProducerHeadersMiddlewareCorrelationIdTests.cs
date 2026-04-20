using System.Diagnostics;
using System.Text;
using KafkaFlow;
using NSubstitute;
using Platform.KafkaFlow.ProducerHeaders;
using Platform.Messaging.Abstractions;
using Platform.ServiceDefaults.CorrelationId;

namespace Platform.KafkaFlow.ProducerHeaders.UnitTests;

public class ProducerHeadersMiddlewareCorrelationIdTests
{
    private const string TestOrigin = "Platform.KafkaFlow.ProducerHeaders.UnitTests";

    [Fact]
    public async Task Invoke_WhenHeaderAlreadyPresent_DoesNotOverwrite()
    {
        // Arrange — caller has already set the header explicitly; the middleware must preserve it.
        var caller = Guid.CreateVersion7().ToString();
        var (context, headers) = BuildContext();
        headers.Add(MessageHeaderKeys.CorrelationId, Encoding.UTF8.GetBytes(caller));

        var middleware = new ProducerHeadersMiddleware(new ProducerHeadersOptions { Origin = TestOrigin });

        // Act
        await middleware.Invoke(context, _ => Task.CompletedTask);

        // Assert
        HeaderValue(headers, MessageHeaderKeys.CorrelationId).Should().Be(caller);
    }

    [Fact]
    public async Task Invoke_WhenHeaderAbsentAndActivityTagSet_WritesTagValue()
    {
        // Arrange — HTTP middleware (M2) or consumer middleware has set Activity.Current tag.
        var ambient = Guid.CreateVersion7().ToString();
        using var source = new ActivitySource("Platform.KafkaFlow.ProducerHeaders.UnitTests");
        using var listener = CreateAllDataListener();
        ActivitySource.AddActivityListener(listener);
        using var activity = source.StartActivity("produce")!;
        activity.SetTag(CorrelationIdContextKeys.ActivityTagName, ambient);

        var (context, headers) = BuildContext();
        var middleware = new ProducerHeadersMiddleware(new ProducerHeadersOptions { Origin = TestOrigin });

        // Act
        await middleware.Invoke(context, _ => Task.CompletedTask);

        // Assert
        HeaderValue(headers, MessageHeaderKeys.CorrelationId).Should().Be(ambient);
    }

    [Fact]
    public async Task Invoke_WhenHeaderAbsentAndActivityTagAbsent_GeneratesUuidV7()
    {
        // Arrange — no ambient correlation id anywhere; middleware originates a new workflow id.
        using var source = new ActivitySource("Platform.KafkaFlow.ProducerHeaders.UnitTests");
        using var listener = CreateAllDataListener();
        ActivitySource.AddActivityListener(listener);
        using var activity = source.StartActivity("produce")!;

        var (context, headers) = BuildContext();
        var middleware = new ProducerHeadersMiddleware(new ProducerHeadersOptions { Origin = TestOrigin });

        // Act
        await middleware.Invoke(context, _ => Task.CompletedTask);

        // Assert
        var written = HeaderValue(headers, MessageHeaderKeys.CorrelationId);
        using (new AssertionScope())
        {
            written.Should().NotBeNullOrEmpty();
            Guid.TryParse(written, out var parsed).Should().BeTrue();
            IsUuidV7(parsed).Should().BeTrue();
        }
    }

    [Fact]
    public async Task Invoke_WhenHeaderAbsentAndNoActivity_GeneratesUuidV7()
    {
        // Arrange — no Activity.Current (background worker, outbox relay worker service).
        Activity.Current.Should().BeNull("this test asserts behaviour without ambient Activity");

        var (context, headers) = BuildContext();
        var middleware = new ProducerHeadersMiddleware(new ProducerHeadersOptions { Origin = TestOrigin });

        // Act
        await middleware.Invoke(context, _ => Task.CompletedTask);

        // Assert
        var written = HeaderValue(headers, MessageHeaderKeys.CorrelationId);
        using (new AssertionScope())
        {
            written.Should().NotBeNullOrEmpty();
            Guid.TryParse(written, out var parsed).Should().BeTrue();
            IsUuidV7(parsed).Should().BeTrue();
        }
    }

    [Fact]
    public async Task Invoke_WhenHeaderAndActivityTagBothPresent_PreservesHeaderAndIgnoresActivity()
    {
        // Arrange — caller supplied an explicit header AND an Activity tag is set. Caller wins.
        var callerHeader = Guid.CreateVersion7().ToString();
        var activityTag = Guid.CreateVersion7().ToString();
        callerHeader.Should().NotBe(activityTag, "the two values must differ for the test to be meaningful");

        using var source = new ActivitySource("Platform.KafkaFlow.ProducerHeaders.UnitTests");
        using var listener = CreateAllDataListener();
        ActivitySource.AddActivityListener(listener);
        using var activity = source.StartActivity("produce")!;
        activity.SetTag(CorrelationIdContextKeys.ActivityTagName, activityTag);

        var (context, headers) = BuildContext();
        headers.Add(MessageHeaderKeys.CorrelationId, Encoding.UTF8.GetBytes(callerHeader));
        var middleware = new ProducerHeadersMiddleware(new ProducerHeadersOptions { Origin = TestOrigin });

        // Act
        await middleware.Invoke(context, _ => Task.CompletedTask);

        // Assert
        HeaderValue(headers, MessageHeaderKeys.CorrelationId).Should().Be(callerHeader);
    }

    [Fact]
    public async Task Invoke_AlwaysWritesMessageIdAndOrigin_RegardlessOfCorrelationIdPath()
    {
        // Arrange — regression: existing message.id + origin behaviour is preserved on every path.
        var (context, headers) = BuildContext();
        var middleware = new ProducerHeadersMiddleware(new ProducerHeadersOptions { Origin = TestOrigin });

        // Act
        await middleware.Invoke(context, _ => Task.CompletedTask);

        // Assert
        using (new AssertionScope())
        {
            HeaderValue(headers, MessageHeaderKeys.MessageId).Should().NotBeNullOrEmpty();
            HeaderValue(headers, MessageHeaderKeys.Origin).Should().Be(TestOrigin);
            HeaderValue(headers, MessageHeaderKeys.CorrelationId).Should().NotBeNullOrEmpty();
        }
    }

    private static (IMessageContext Context, MessageHeaders Headers) BuildContext()
    {
        var headers = new MessageHeaders();
        var context = Substitute.For<IMessageContext>();
        context.Headers.Returns(headers);
        return (context, headers);
    }

    private static string? HeaderValue(MessageHeaders headers, string key)
    {
        var header = headers.FirstOrDefault(h => h.Key == key);
        return header.Value is null ? null : Encoding.UTF8.GetString(header.Value);
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
}
