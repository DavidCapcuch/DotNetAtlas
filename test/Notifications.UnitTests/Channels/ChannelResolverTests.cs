using AwesomeAssertions;
using Notifications.Domain.Channels;
using Xunit;

namespace Notifications.UnitTests.Channels;

/// <summary>
/// The channel-resolution rule (notifications.md § 5.3): <c>resolved = enabled_channels ∩ template_channels</c>.
/// A template fires only on a channel it has content for, intersected with what the recipient enabled.
/// </summary>
public sealed class ChannelResolverTests
{
    [Fact]
    public void Resolve_KeepsTheChannelsInBothSets()
    {
        // Arrange
        var enabled = new[] { ChannelType.Email, ChannelType.Sms, ChannelType.Bell };
        var supported = new[] { ChannelType.Email, ChannelType.Bell };

        // Act
        var resolved = ChannelResolver.Resolve(enabled, supported);

        // Assert
        resolved.Should().Equal(ChannelType.Email, ChannelType.Bell);
    }

    [Fact]
    public void Resolve_WhenSetsDisjoint_ReturnsEmpty()
    {
        // Arrange — the user enabled only a channel the template does not support.
        var enabled = new[] { ChannelType.Sms };
        var supported = new[] { ChannelType.Email };

        // Act
        var resolved = ChannelResolver.Resolve(enabled, supported);

        // Assert
        resolved.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_DropsChannelTheTemplateDoesNotSupport()
    {
        // Arrange — the user enabled Sms, but the template has no Sms content.
        var enabled = new[] { ChannelType.Email, ChannelType.Sms };
        var supported = new[] { ChannelType.Email };

        // Act
        var resolved = ChannelResolver.Resolve(enabled, supported);

        // Assert
        resolved.Should().Equal(ChannelType.Email);
    }

    [Fact]
    public void Resolve_DropsChannelTheUserDisabled()
    {
        // Arrange — the template supports Sms, but the user did not enable it.
        var enabled = new[] { ChannelType.Email };
        var supported = new[] { ChannelType.Email, ChannelType.Sms };

        // Act
        var resolved = ChannelResolver.Resolve(enabled, supported);

        // Assert
        resolved.Should().Equal(ChannelType.Email);
    }

    [Fact]
    public void Resolve_IsDeterministicRegardlessOfInputOrder()
    {
        // Arrange — same sets, different orders, must yield the same ordered result.
        var enabled = new[] { ChannelType.Bell, ChannelType.Email, ChannelType.Sms };
        var supported = new[] { ChannelType.Sms, ChannelType.Bell, ChannelType.Email };

        // Act
        var resolved = ChannelResolver.Resolve(enabled, supported);

        // Assert — canonical order by SmartEnum value (Email=1, Sms=2, Bell=3).
        resolved.Should().Equal(ChannelType.Email, ChannelType.Sms, ChannelType.Bell);
    }

    [Fact]
    public void Resolve_WhenNoChannelsEnabled_ReturnsEmpty()
    {
        // Act
        var resolved = ChannelResolver.Resolve([], [ChannelType.Email, ChannelType.Bell]);

        // Assert
        resolved.Should().BeEmpty();
    }
}
