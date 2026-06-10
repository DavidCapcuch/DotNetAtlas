using AwesomeAssertions;
using Notifications.Domain.Channels;
using Xunit;

namespace Notifications.UnitTests.Channels;

public sealed class ChannelTypeTests
{
    [Fact]
    public void Email_DoesNotRespectQuietHours()
    {
        ChannelType.Email.RespectsQuietHours.Should().BeFalse();
    }

    [Fact]
    public void Sms_RespectsQuietHours()
    {
        ChannelType.Sms.RespectsQuietHours.Should().BeTrue();
    }

    [Fact]
    public void Bell_DoesNotRespectQuietHours()
    {
        ChannelType.Bell.RespectsQuietHours.Should().BeFalse();
    }

    [Fact]
    public void Email_IsDurable()
    {
        ChannelType.Email.IsDurable.Should().BeTrue();
    }

    [Fact]
    public void Sms_IsDurable()
    {
        ChannelType.Sms.IsDurable.Should().BeTrue();
    }

    [Fact]
    public void Bell_IsNotDurable()
    {
        ChannelType.Bell.IsDurable.Should().BeFalse();
    }

    [Fact]
    public void FromName_RoundTripsTheChannel()
    {
        ChannelType.FromName("Email").Should().BeSameAs(ChannelType.Email);
        ChannelType.FromName("Sms").Should().BeSameAs(ChannelType.Sms);
        ChannelType.FromName("Bell").Should().BeSameAs(ChannelType.Bell);
    }

    [Fact]
    public void List_ContainsExactlyTheThreeV2Channels()
    {
        ChannelType.List.Should().BeEquivalentTo([ChannelType.Email, ChannelType.Sms, ChannelType.Bell]);
    }
}
