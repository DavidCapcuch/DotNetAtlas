using AwesomeAssertions;
using Notifications.Domain.Channels;
using Notifications.Domain.Deliveries;
using Xunit;

namespace Notifications.UnitTests.Deliveries;

public sealed class NotificationDeliveryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 4, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddMinutes(5);

    [Fact]
    public void Record_Failed_IsNotDispatched_AndStampsBothTimes()
    {
        var delivery = NotificationDelivery.Record(Guid.CreateVersion7(), ChannelType.Email, DeliveryStatus.Failed, T0);

        using (new AssertionScope())
        {
            delivery.IsDispatched.Should().BeFalse();
            delivery.Status.Should().Be(DeliveryStatus.Failed);
            delivery.CreatedAtUtc.Should().Be(T0);
            delivery.UpdatedAtUtc.Should().Be(T0);
        }
    }

    [Fact]
    public void MarkDispatched_FlipsAFailedRow_WithoutMovingCreatedAt()
    {
        var delivery = NotificationDelivery.Record(Guid.CreateVersion7(), ChannelType.Email, DeliveryStatus.Failed, T0);

        delivery.MarkDispatched(T1);

        using (new AssertionScope())
        {
            delivery.IsDispatched.Should().BeTrue();
            delivery.Status.Should().Be(DeliveryStatus.Dispatched);
            delivery.CreatedAtUtc.Should().Be(T0);
            delivery.UpdatedAtUtc.Should().Be(T1);
        }
    }

    [Fact]
    public void Record_Dispatched_IsDispatched()
    {
        var delivery = NotificationDelivery.Record(Guid.CreateVersion7(), ChannelType.Email, DeliveryStatus.Dispatched, T0);

        delivery.IsDispatched.Should().BeTrue();
    }
}
