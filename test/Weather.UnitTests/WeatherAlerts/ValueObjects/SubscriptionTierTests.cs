using Weather.Domain.Alerts.ValueObjects;

namespace Weather.UnitTests.WeatherAlerts.ValueObjects;

public class SubscriptionTierTests
{
    [Fact]
    public void FreeTier_HasCorrectMaxSubscriptions()
    {
        // Arrange & Act
        var freeTier = SubscriptionTier.Free;

        // Assert
        freeTier.MaxSubscriptions.Should().Be(5);
    }

    [Fact]
    public void ProTier_HasCorrectMaxSubscriptions()
    {
        // Arrange & Act
        var proTier = SubscriptionTier.Pro;

        // Assert
        proTier.MaxSubscriptions.Should().Be(25);
    }

    [Fact]
    public void UltraTier_HasCorrectMaxSubscriptions()
    {
        // Arrange & Act
        var ultraTier = SubscriptionTier.Ultra;

        // Assert
        ultraTier.MaxSubscriptions.Should().Be(100);
    }
}
