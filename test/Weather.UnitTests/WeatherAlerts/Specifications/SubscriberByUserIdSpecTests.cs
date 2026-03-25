using Ardalis.Specification.EntityFrameworkCore;
using Weather.Domain.Alerts;
using Weather.Domain.Alerts.Specifications;
using Weather.Domain.Alerts.ValueObjects;

namespace Weather.UnitTests.WeatherAlerts.Specifications;

public class SubscriberByUserIdSpecTests
{
    private static readonly DateTimeOffset UtcNow = DateTimeOffset.UtcNow;

    [Fact]
    public void WhenApplied_ShouldFilterByUserId()
    {
        // Arrange
        var targetUserId = Guid.CreateVersion7();
        var targetAlertSubscriber = AlertSubscriber.CreateFree(targetUserId);
        var otherAlertSubscriber1 = AlertSubscriber.CreateWithPaidSubscription(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), SubscriptionTier.Pro, 30, UtcNow);
        var otherAlertSubscriber2 = AlertSubscriber.CreateWithPaidSubscription(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), SubscriptionTier.Ultra, 30, UtcNow);

        var alertSubscribers = new List<AlertSubscriber>
        {
            targetAlertSubscriber,
            otherAlertSubscriber1,
            otherAlertSubscriber2
        };

        var subscriberByUserIdSpec = new SubscriberByUserIdSpec(targetUserId);

        // Act
        var filteredAlertSubscribers = alertSubscribers
            .AsQueryable()
            .WithSpecification(subscriberByUserIdSpec)
            .ToList();

        // Assert
        using (new AssertionScope())
        {
            filteredAlertSubscribers.Should().ContainSingle();
            filteredAlertSubscribers.Single().UserId.Should().Be(targetUserId);
        }
    }

    [Fact]
    public void WhenNoMatchingUserId_ShouldReturnEmpty()
    {
        // Arrange
        var alertSubscriber1 = AlertSubscriber.CreateFree(Guid.CreateVersion7());
        var alertSubscriber2 = AlertSubscriber.CreateWithPaidSubscription(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), SubscriptionTier.Pro, 30, UtcNow);

        var alertSubscribers = new List<AlertSubscriber>
        {
            alertSubscriber1,
            alertSubscriber2
        };
        var nonExistentUserId = Guid.CreateVersion7();
        var subscriberByUserIdSpec = new SubscriberByUserIdSpec(nonExistentUserId);

        // Act
        var filteredAlertSubscribers = alertSubscribers
            .AsQueryable()
            .WithSpecification(subscriberByUserIdSpec)
            .ToList();

        // Assert
        filteredAlertSubscribers.Should().BeEmpty();
    }
}
