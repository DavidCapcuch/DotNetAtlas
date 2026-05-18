using Ardalis.Specification.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Weather.Domain.Alerts;
using Weather.Domain.Alerts.Specifications;
using Weather.Domain.Alerts.ValueObjects;

namespace Weather.UnitTests.WeatherAlerts.Specifications;

public class ExpiredPremiumSubscribersSpecTests
{
    private readonly FakeTimeProvider _fakeTimeProvider = new();

    [Fact]
    public void WhenApplied_ShouldFilterExpiredPremiumSubscribers()
    {
        // Arrange
        var currentUtc = _fakeTimeProvider.GetUtcNow();
        // Create subscriptions that expired at different times
        // Expired 1 day ago: created 2 days ago with 1 day duration
        var expiredProAlertSubscriber = AlertSubscriber.CreateWithPaidSubscription(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), SubscriptionTier.Pro, 1, currentUtc.AddDays(-2));
        // Expired 5 days ago: created 6 days ago with 1 day duration
        var expiredUltraAlertSubscriber = AlertSubscriber.CreateWithPaidSubscription(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), SubscriptionTier.Ultra, 1, currentUtc.AddDays(-6));
        // Active: expires in 10 days
        var activeProAlertSubscriber = AlertSubscriber.CreateWithPaidSubscription(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), SubscriptionTier.Pro, 10, currentUtc);
        var freeAlertSubscriber = AlertSubscriber.CreateFree(Guid.CreateVersion7(), currentUtc);

        var alertSubscribers = new List<AlertSubscriber>
        {
            expiredProAlertSubscriber,
            expiredUltraAlertSubscriber,
            activeProAlertSubscriber,
            freeAlertSubscriber
        };

        var expiredPremiumSubscribersSpec = new ExpiredPremiumSubscribersSpec(currentUtc);

        // Act
        var filteredExpiredPremiumSubscribers = alertSubscribers
            .AsQueryable()
            .WithSpecification(expiredPremiumSubscribersSpec)
            .ToList();

        // Assert
        filteredExpiredPremiumSubscribers.Should()
            .HaveCount(2)
            .And.Contain(expiredProAlertSubscriber)
            .And.Contain(expiredUltraAlertSubscriber)
            .And.NotContain(activeProAlertSubscriber)
            .And.NotContain(freeAlertSubscriber);
    }

    [Fact]
    public void WhenNoExpiredSubscribers_ShouldReturnEmpty()
    {
        // Arrange
        var currentUtc = _fakeTimeProvider.GetUtcNow();
        var activeProAlertSubscriber = AlertSubscriber.CreateWithPaidSubscription(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), SubscriptionTier.Pro, 10, currentUtc);
        var freeAlertSubscriber = AlertSubscriber.CreateFree(Guid.CreateVersion7(), currentUtc);

        var alertSubscribers = new List<AlertSubscriber>
        {
            activeProAlertSubscriber,
            freeAlertSubscriber
        };

        var expiredPremiumSubscribersSpec = new ExpiredPremiumSubscribersSpec(currentUtc);

        // Act
        var filteredExpiredPremiumSubscribers = alertSubscribers
            .AsQueryable()
            .WithSpecification(expiredPremiumSubscribersSpec)
            .ToList();

        // Assert
        filteredExpiredPremiumSubscribers.Should().BeEmpty();
    }

    [Fact]
    public void WhenExpiryExactlyAtCurrentTime_ShouldIncludeInResult()
    {
        // Arrange
        var currentUtc = _fakeTimeProvider.GetUtcNow();
        // Create subscription that expires exactly at currentUtc: created 1 day ago with 1 day duration
        var exactlyExpiredAlertSubscriber = AlertSubscriber.CreateWithPaidSubscription(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), SubscriptionTier.Pro, 1, currentUtc.AddDays(-1));

        var alertSubscribers = new List<AlertSubscriber>
        {
            exactlyExpiredAlertSubscriber
        };
        var expiredPremiumSubscribersSpec = new ExpiredPremiumSubscribersSpec(currentUtc);

        // Act
        var filteredExpiredPremiumSubscribers = alertSubscribers
            .AsQueryable()
            .WithSpecification(expiredPremiumSubscribersSpec)
            .ToList();

        // Assert
        filteredExpiredPremiumSubscribers.Should().ContainSingle().Which.Should().Be(exactlyExpiredAlertSubscriber);
    }
}
