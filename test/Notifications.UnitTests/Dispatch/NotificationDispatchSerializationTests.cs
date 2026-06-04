using AwesomeAssertions;
using Hangfire;
using Hangfire.Common;
using Notifications.Application.Dispatch;
using Xunit;

namespace Notifications.UnitTests.Dispatch;

/// <summary>
/// The Kafka handler enqueues <see cref="NotificationDispatch"/> as a Hangfire job argument, so it is
/// serialized → persisted → deserialized before the dispatcher ever runs. The dispatcher-direct
/// integration seam bypasses Hangfire, so this guards the round-trip: a <c>required</c>-init record +
/// a <see cref="Dictionary{TKey,TValue}"/> payload must survive Hangfire's configured serializer
/// (the recommended settings we apply in <c>BackgroundJobsDependencyInjection</c>).
/// </summary>
public sealed class NotificationDispatchSerializationTests
{
    [Fact]
    public void NotificationDispatch_RoundTrips_ThroughHangfireSerializer()
    {
        // Mirror the production serializer configuration (BackgroundJobsDependencyInjection).
        GlobalConfiguration.Configuration.UseRecommendedSerializerSettings();

        var original = new NotificationDispatch
        {
            NotificationId = Guid.CreateVersion7(),
            RecipientUserId = Guid.CreateVersion7(),
            TemplateKey = "invoicing.invoice-delivered",
            Payload = new Dictionary<string, string>
            {
                ["InvoiceNumber"] = "INV-2026-000042",
                ["TotalAmount"] = "152.00",
                ["ViewInvoiceUrl"] = "https://invoicing.example.com/invoices/00000000-0000-0000-0000-000000000001",
            },
        };

        var json = SerializationHelper.Serialize(original, SerializationOption.User);
        var restored = SerializationHelper.Deserialize<NotificationDispatch>(json, SerializationOption.User);

        restored.Should().NotBeNull();
        restored!.NotificationId.Should().Be(original.NotificationId);
        restored.RecipientUserId.Should().Be(original.RecipientUserId);
        restored.TemplateKey.Should().Be(original.TemplateKey);
        restored.Payload.Should().BeEquivalentTo(original.Payload);
    }
}
