using System.Text;
using KafkaFlow;
using NSubstitute;
using Platform.Messaging.Abstractions;

namespace Platform.KafkaFlow.ProducerHeaders.UnitTests;

/// <summary>
/// Pins the <see cref="ProducerHeadersMiddleware"/> contract: every produced message carries
/// <c>message.id</c> (a UUID v7 for idempotent processing) and <c>origin</c>, and existing values
/// are never overwritten. Cross-process correlation rides W3C <c>traceparent</c> (OpenTelemetry).
/// </summary>
public class ProducerHeadersMiddlewareTests
{
    private const string TestOrigin = "Platform.KafkaFlow.ProducerHeaders.UnitTests";

    [Fact]
    public async Task Invoke_WhenHeadersAbsent_WritesMessageIdAndOrigin()
    {
        // Arrange
        var (context, headers) = BuildContext();
        var middleware = new ProducerHeadersMiddleware(new ProducerHeadersOptions { Origin = TestOrigin });

        // Act
        await middleware.Invoke(context, _ => Task.CompletedTask);

        // Assert
        using (new AssertionScope())
        {
            var messageId = HeaderValue(headers, MessageHeaderKeys.MessageId);
            messageId.Should().NotBeNullOrEmpty();
            Guid.TryParse(messageId, out var parsed).Should().BeTrue();
            IsUuidV7(parsed).Should().BeTrue("message.id is a UUID v7 for idempotent processing");

            HeaderValue(headers, MessageHeaderKeys.Origin).Should().Be(TestOrigin);
        }
    }

    [Fact]
    public async Task Invoke_WhenMessageIdAndOriginAlreadyPresent_DoesNotOverwrite()
    {
        // Arrange — an upstream already stamped both headers (e.g. a re-produced message).
        var callerMessageId = Guid.CreateVersion7().ToString();
        const string callerOrigin = "upstream-service";
        var (context, headers) = BuildContext();
        headers.Add(MessageHeaderKeys.MessageId, Encoding.UTF8.GetBytes(callerMessageId));
        headers.Add(MessageHeaderKeys.Origin, Encoding.UTF8.GetBytes(callerOrigin));
        var middleware = new ProducerHeadersMiddleware(new ProducerHeadersOptions { Origin = TestOrigin });

        // Act
        await middleware.Invoke(context, _ => Task.CompletedTask);

        // Assert
        using (new AssertionScope())
        {
            HeaderValue(headers, MessageHeaderKeys.MessageId).Should().Be(callerMessageId,
                "an existing message.id must be preserved for idempotency");
            HeaderValue(headers, MessageHeaderKeys.Origin).Should().Be(callerOrigin,
                "an existing origin must be preserved");
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

    private static bool IsUuidV7(Guid guid)
    {
        Span<byte> bytes = stackalloc byte[16];
        guid.TryWriteBytes(bytes, bigEndian: true, out _);
        return (bytes[6] >> 4) == 0x7;
    }
}
