using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Platform.SharedKernel.Errors;
using Platform.SharedKernel.Exceptions;
using Weather.Domain.Alerts;
using Weather.Domain.Alerts.Events;
using Weather.Domain.Alerts.ValueObjects;

namespace Weather.UnitTests.WeatherAlerts.Aggregates;

public class AlertSubscriberTests
{
    private readonly FakeTimeProvider _fakeTimeProvider = new();
    private DateTimeOffset UtcNow => _fakeTimeProvider.GetUtcNow();

    [Fact]
    public void CreateFree_WhenValidInput_ReturnsSubscriberWithFreeTierAndRaisesEvent()
    {
        // Arrange
        var userId = Guid.CreateVersion7();

        // Act
        var alertSubscriber = AlertSubscriber.CreateFree(userId);

        // Assert
        using (new AssertionScope())
        {
            alertSubscriber.Should().NotBeNull();
            alertSubscriber.UserId.Should().Be(userId);
            alertSubscriber.SubscriptionTier.Should().Be(SubscriptionTier.Free);
            alertSubscriber.SubscriptionExpiryAtUtc.Should().BeNull();
            alertSubscriber.TemperatureUnitPreference.Should().Be(TemperatureUnit.Celsius);
            alertSubscriber.MonitoredLocationAlertsSubscriptions.Should().BeEmpty();
            alertSubscriber.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<SubscriberCreatedDomainEvent>();
        }
    }

    [Fact]
    public void CreateWithPaidSubscription_WhenValidInput_ReturnsSubscriberWithPaidTierAndRaisesEvent()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var correlationId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();
        const int durationDays = 30;
        var expectedExpiryDate = UtcNow.AddDays(durationDays);

        // Act
        var alertSubscriber = AlertSubscriber.CreateWithPaidSubscription(userId, correlationId, paymentTransactionId,
            SubscriptionTier.Pro, durationDays, UtcNow);

        // Assert
        using (new AssertionScope())
        {
            alertSubscriber.Should().NotBeNull();
            alertSubscriber.UserId.Should().Be(userId);
            alertSubscriber.SubscriptionTier.Should().Be(SubscriptionTier.Pro);
            alertSubscriber.SubscriptionExpiryAtUtc.Should().Be(expectedExpiryDate);
            alertSubscriber.MonitoredLocationAlertsSubscriptions.Should().BeEmpty();
            var domainEvent = alertSubscriber.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<SubscriberActivatedDomainEvent>()
                .Subject;
            domainEvent.CorrelationId.Should().Be(correlationId);
            domainEvent.PaymentTransactionId.Should().Be(paymentTransactionId);
            domainEvent.DurationDays.Should().Be(durationDays);
        }
    }

    [Fact]
    public void CreateWithPaidSubscription_WhenFreeTier_ThrowsDataIntegrityException()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var correlationId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();

        // Act
        var createAction = () =>
            AlertSubscriber.CreateWithPaidSubscription(userId, correlationId, paymentTransactionId, SubscriptionTier.Free, 30, UtcNow);

        // Assert
        createAction.Should()
            .Throw<DataIntegrityException>()
            .WithMessage("*Cannot create paid subscription with Free tier*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-30)]
    public void CreateWithPaidSubscription_WhenInvalidDuration_ThrowsDataIntegrityException(int durationDays)
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var correlationId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();

        // Act
        var createAction = () =>
            AlertSubscriber.CreateWithPaidSubscription(userId, correlationId, paymentTransactionId, SubscriptionTier.Pro, durationDays,
                UtcNow);

        // Assert
        createAction.Should()
            .Throw<DataIntegrityException>()
            .WithMessage("*Subscription duration must be greater than zero*");
    }

    [Fact]
    public void SubscribeToMonitoredLocation_WhenUnderLimit_AddsSubscriptionAndRaisesEvent()
    {
        // Arrange
        var alertSubscriber = AlertSubscriber.CreateFree(Guid.CreateVersion7());
        _ = alertSubscriber.PopDomainEvents(); // Clear creation event
        var monitoredLocationId = Guid.CreateVersion7();

        // Act
        var subscribeResult = alertSubscriber.SubscribeToMonitoredLocation(monitoredLocationId);

        // Assert
        using (new AssertionScope())
        {
            subscribeResult.Should().BeSuccess();
            alertSubscriber.MonitoredLocationAlertsSubscriptions.Should().ContainSingle();
            alertSubscriber.MonitoredLocationAlertsSubscriptions.First().MonitoredLocationId.Should()
                .Be(monitoredLocationId);
            alertSubscriber.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<MonitoredLocationAlertsSubscriptionCreatedDomainEvent>();
        }
    }

    [Fact]
    public void SubscribeToMonitoredLocation_WhenAlreadySubscribed_ReturnsOkWithoutDuplicate()
    {
        // Arrange
        var alertSubscriber = AlertSubscriber.CreateFree(Guid.CreateVersion7());
        var monitoredLocationId = Guid.CreateVersion7();
        alertSubscriber.SubscribeToMonitoredLocation(monitoredLocationId);
        _ = alertSubscriber.PopDomainEvents(); // Clear events

        // Act
        var subscribeResult = alertSubscriber.SubscribeToMonitoredLocation(monitoredLocationId);

        // Assert
        using (new AssertionScope())
        {
            subscribeResult.Should().BeSuccess();
            alertSubscriber.MonitoredLocationAlertsSubscriptions.Should().ContainSingle();
            alertSubscriber.PopDomainEvents().Should().BeEmpty();
        }
    }

    [Fact]
    public void SubscribeToMonitoredLocation_WhenAtMaxSubscriptions_ReturnsMaxReachedError()
    {
        // Arrange
        var alertSubscriber = AlertSubscriber.CreateFree(Guid.CreateVersion7());
        for (var i = 0; i < SubscriptionTier.Free.MaxSubscriptions; i++)
        {
            alertSubscriber.SubscribeToMonitoredLocation(Guid.CreateVersion7());
        }

        _ = alertSubscriber.PopDomainEvents(); // Clear events
        var extraLocationId = Guid.CreateVersion7();

        // Act
        var subscribeResult = alertSubscriber.SubscribeToMonitoredLocation(extraLocationId);

        // Assert
        using (new AssertionScope())
        {
            subscribeResult.Should().BeFailure();
            var validationError = subscribeResult.Errors[0] as ValidationError;
            validationError.Should().NotBeNull();
            validationError!.ErrorCode.Should().Be("Alert.MaxSubscriptionsReached");
            alertSubscriber.MonitoredLocationAlertsSubscriptions.Should()
                .HaveCount(SubscriptionTier.Free.MaxSubscriptions);
        }
    }

    [Fact]
    public void UnsubscribeFromMonitoredLocation_WhenSubscribed_RemovesAndRaisesEvent()
    {
        // Arrange
        var alertSubscriber = AlertSubscriber.CreateFree(Guid.CreateVersion7());
        var monitoredLocationId = Guid.CreateVersion7();
        alertSubscriber.SubscribeToMonitoredLocation(monitoredLocationId);
        _ = alertSubscriber.PopDomainEvents(); // Clear events

        // Act
        var unsubscribeResult = alertSubscriber.UnsubscribeFromMonitoredLocation(monitoredLocationId);

        // Assert
        using (new AssertionScope())
        {
            unsubscribeResult.Should().BeSuccess();
            alertSubscriber.MonitoredLocationAlertsSubscriptions.Should().BeEmpty();
            alertSubscriber.PopDomainEvents().Should().ContainSingle()
                .Which.Should().BeOfType<MonitoredLocationAlertsSubscriptionRemovedDomainEvent>();
        }
    }

    [Fact]
    public void UnsubscribeFromMonitoredLocation_WhenNotSubscribed_ReturnsNotSubscribedError()
    {
        // Arrange
        var alertSubscriber = AlertSubscriber.CreateFree(Guid.CreateVersion7());
        var monitoredLocationId = Guid.CreateVersion7();

        // Act
        var unsubscribeResult = alertSubscriber.UnsubscribeFromMonitoredLocation(monitoredLocationId);

        // Assert
        unsubscribeResult.Should().BeSuccess();
    }

    [Fact]
    public void ActivatePaidSubscription_FirstTimeSubscriber_RaisesSubscriberActivatedEvent()
    {
        // Arrange
        var alertSubscriber = AlertSubscriber.CreateFree(Guid.CreateVersion7());
        _ = alertSubscriber.PopDomainEvents(); // Clear creation event
        var correlationId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();
        const int durationDays = 30;
        var expectedExpiryDate = UtcNow.AddDays(durationDays);

        // Act
        alertSubscriber.ActivatePaidSubscription(correlationId, paymentTransactionId, SubscriptionTier.Pro, durationDays, UtcNow);

        // Assert
        using (new AssertionScope())
        {
            alertSubscriber.SubscriptionTier.Should().Be(SubscriptionTier.Pro);
            alertSubscriber.SubscriptionExpiryAtUtc.Should().Be(expectedExpiryDate);
            alertSubscriber.LastPaidSubscriptionEndedAtUtc.Should().BeNull();
            var domainEvent = alertSubscriber.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<SubscriberActivatedDomainEvent>()
                .Subject;
            domainEvent.CorrelationId.Should().Be(correlationId);
            domainEvent.PaymentTransactionId.Should().Be(paymentTransactionId);
            domainEvent.DurationDays.Should().Be(durationDays);
        }
    }

    [Fact]
    public void ActivatePaidSubscription_ReturningSubscriber_RaisesSubscriberReactivatedEvent()
    {
        // Arrange
        var alertSubscriber = AlertSubscriber.CreateFree(Guid.CreateVersion7());
        // Create an expired subscription (negative duration from a past time)
        var pastTime = UtcNow.AddDays(-10);
        var expiredSubscriptionDate = pastTime.AddDays(1); // Will expire 9 days ago
        alertSubscriber.ActivatePaidSubscription(Guid.CreateVersion7(), Guid.CreateVersion7(), SubscriptionTier.Pro, 1, pastTime);
        alertSubscriber.DowngradeToFree(UtcNow); // This sets LastPaidSubscriptionEndedAtUtc
        _ = alertSubscriber.PopDomainEvents(); // Clear events

        var correlationId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();
        const int newDurationDays = 30;
        var expectedNewExpiryDate = UtcNow.AddDays(newDurationDays);

        // Act
        alertSubscriber.ActivatePaidSubscription(correlationId, paymentTransactionId, SubscriptionTier.Pro, newDurationDays, UtcNow);

        // Assert
        using (new AssertionScope())
        {
            alertSubscriber.SubscriptionTier.Should().Be(SubscriptionTier.Pro);
            alertSubscriber.SubscriptionExpiryAtUtc.Should().Be(expectedNewExpiryDate);
            var domainEvent = alertSubscriber.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<SubscriberReactivatedDomainEvent>()
                .Subject;
            domainEvent.PreviousSubscriptionExpiredAtUtc.Should().Be(expiredSubscriptionDate);
            domainEvent.CorrelationId.Should().Be(correlationId);
            domainEvent.PaymentTransactionId.Should().Be(paymentTransactionId);
            domainEvent.DurationDays.Should().Be(newDurationDays);
        }
    }

    [Fact]
    public void ActivatePaidSubscription_ExistingPaidSubscriber_RaisesSubscriptionUpgradedEvent()
    {
        // Arrange
        var alertSubscriber = AlertSubscriber.CreateFree(Guid.CreateVersion7());
        alertSubscriber.ActivatePaidSubscription(Guid.CreateVersion7(), Guid.CreateVersion7(), SubscriptionTier.Pro, 30, UtcNow);
        _ = alertSubscriber.PopDomainEvents(); // Clear events

        var correlationId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();
        const int newDurationDays = 60;
        var expectedNewExpiryDate = UtcNow.AddDays(newDurationDays);

        // Act
        alertSubscriber.ActivatePaidSubscription(correlationId, paymentTransactionId, SubscriptionTier.Ultra, newDurationDays, UtcNow);

        // Assert
        using (new AssertionScope())
        {
            alertSubscriber.SubscriptionTier.Should().Be(SubscriptionTier.Ultra);
            alertSubscriber.SubscriptionExpiryAtUtc.Should().Be(expectedNewExpiryDate);
            var domainEvent = alertSubscriber.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<SubscriptionUpgradedDomainEvent>()
                .Subject;
            domainEvent.PreviousTier.Should().Be(SubscriptionTier.Pro);
            domainEvent.NewTier.Should().Be(SubscriptionTier.Ultra);
            domainEvent.CorrelationId.Should().Be(correlationId);
            domainEvent.PaymentTransactionId.Should().Be(paymentTransactionId);
            domainEvent.DurationDays.Should().Be(newDurationDays);
        }
    }

    [Fact]
    public void ActivatePaidSubscription_ToFreeTier_ThrowsDataIntegrityException()
    {
        // Arrange
        var alertSubscriber = AlertSubscriber.CreateWithPaidSubscription(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            SubscriptionTier.Pro, 30, UtcNow);

        // Act
        var activatePaidSubscriptionAction = () =>
            alertSubscriber.ActivatePaidSubscription(Guid.CreateVersion7(), Guid.CreateVersion7(), SubscriptionTier.Free, 30, UtcNow);

        // Assert
        activatePaidSubscriptionAction.Should()
            .Throw<DataIntegrityException>()
            .WithMessage("*Cannot upgrade to Free tier*");
    }

    [Fact]
    public void ActivatePaidSubscription_Downgrade_ThrowsDataIntegrityException()
    {
        // Arrange
        var alertSubscriber = AlertSubscriber.CreateWithPaidSubscription(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            SubscriptionTier.Ultra, 30, UtcNow);

        // Act
        var activatePaidSubscriptionAction = () =>
            alertSubscriber.ActivatePaidSubscription(Guid.CreateVersion7(), Guid.CreateVersion7(), SubscriptionTier.Pro, 60, UtcNow);

        // Assert
        activatePaidSubscriptionAction.Should()
            .Throw<DataIntegrityException>()
            .WithMessage("*Cannot downgrade*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-30)]
    public void ActivatePaidSubscription_WhenInvalidDuration_ThrowsDataIntegrityException(int durationDays)
    {
        // Arrange
        var alertSubscriber = AlertSubscriber.CreateFree(Guid.CreateVersion7());

        // Act
        var activatePaidSubscriptionAction = () =>
            alertSubscriber.ActivatePaidSubscription(Guid.CreateVersion7(), Guid.CreateVersion7(), SubscriptionTier.Pro, durationDays, UtcNow);

        // Assert
        activatePaidSubscriptionAction.Should()
            .Throw<DataIntegrityException>()
            .WithMessage("*Subscription duration must be greater than zero*");
    }

    [Fact]
    public void DowngradeToFree_WhenExpired_RemovesExcessSubscriptionsAndSetsLastPaidSubscriptionEndedAtUtc()
    {
        // Arrange
        var alertSubscriber = AlertSubscriber.CreateFree(Guid.CreateVersion7());
        // Create subscription that expires 1 day ago (activate from 2 days ago with 1 day duration)
        var pastTime = UtcNow.AddDays(-2);
        var expiredSubscriptionDate = pastTime.AddDays(1); // Expired 1 day ago
        alertSubscriber.ActivatePaidSubscription(Guid.CreateVersion7(), Guid.CreateVersion7(), SubscriptionTier.Pro, 1, pastTime);

        // Add subscriptions exceeding the free tier limit (10 subscriptions, more than Free tier's 5)
        for (var i = 0; i < 10; i++)
        {
            alertSubscriber.SubscribeToMonitoredLocation(Guid.CreateVersion7());
        }

        // Act
        var downgradeToFreeResult = alertSubscriber.DowngradeToFree(UtcNow);

        // Assert
        using (new AssertionScope())
        {
            downgradeToFreeResult.Should().BeSuccess();
            alertSubscriber.SubscriptionTier.Should().Be(SubscriptionTier.Free);
            alertSubscriber.SubscriptionExpiryAtUtc.Should().BeNull();
            alertSubscriber.LastPaidSubscriptionEndedAtUtc.Should().Be(expiredSubscriptionDate);
            alertSubscriber.MonitoredLocationAlertsSubscriptions.Should()
                .HaveCount(SubscriptionTier.Free.MaxSubscriptions);
        }
    }

    [Fact]
    public void DowngradeToFree_WhenActive_ReturnsCannotDowngradeError()
    {
        // Arrange
        var alertSubscriber = AlertSubscriber.CreateFree(Guid.CreateVersion7());
        alertSubscriber.ActivatePaidSubscription(Guid.CreateVersion7(), Guid.CreateVersion7(), SubscriptionTier.Pro, 30, UtcNow);

        // Act
        var downgradeToFreeResult = alertSubscriber.DowngradeToFree(UtcNow);

        // Assert
        using (new AssertionScope())
        {
            downgradeToFreeResult.Should().BeFailure();
            var downgradeToFreeValidationError = downgradeToFreeResult.Errors[0] as ValidationError;
            downgradeToFreeValidationError.Should().NotBeNull();
            downgradeToFreeValidationError!.ErrorCode.Should().Be("Alert.CannotDowngradeActiveSubscription");
        }
    }

    [Fact]
    public void DowngradeToFree_WhenAlreadyFree_ReturnsOk()
    {
        // Arrange
        var alertSubscriber = AlertSubscriber.CreateFree(Guid.CreateVersion7());

        // Act
        var downgradeToFreeResult = alertSubscriber.DowngradeToFree(UtcNow);

        // Assert
        downgradeToFreeResult.Should().BeSuccess();
    }

    [Fact]
    public void ExtendSubscription_WhenActive_ExtendsFromCurrentExpiry()
    {
        // Arrange
        const int initialDurationDays = 10;
        const int extensionDurationDays = 30;
        var originalSubscriptionExpiry = UtcNow.AddDays(initialDurationDays);
        var expectedNewSubscriptionExpiry = originalSubscriptionExpiry.AddDays(extensionDurationDays);

        var alertSubscriber = AlertSubscriber.CreateWithPaidSubscription(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            SubscriptionTier.Pro, initialDurationDays, UtcNow);
        var currentUtcNow = UtcNow;
        var correlationId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();

        // Act
        alertSubscriber.ExtendSubscription(correlationId, paymentTransactionId, extensionDurationDays, currentUtcNow);

        // Assert
        alertSubscriber.SubscriptionExpiryAtUtc.Should().Be(expectedNewSubscriptionExpiry);
    }

    [Fact]
    public void ExtendSubscription_WhenExpired_ExtendsFromCurrentTime()
    {
        // Arrange
        // Create subscription that expired 5 days ago (activate from 6 days ago with 1 day duration)
        var pastTime = UtcNow.AddDays(-6);
        var alertSubscriber = AlertSubscriber.CreateWithPaidSubscription(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            SubscriptionTier.Pro, 1, pastTime);
        var currentUtcNow = UtcNow;
        const int extensionDurationDays = 30;
        var expectedSubscriptionExpiry = currentUtcNow.AddDays(extensionDurationDays);
        var correlationId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();

        // Act
        alertSubscriber.ExtendSubscription(correlationId, paymentTransactionId, extensionDurationDays, currentUtcNow);

        // Assert
        alertSubscriber.SubscriptionExpiryAtUtc.Should().Be(expectedSubscriptionExpiry);
    }

    [Fact]
    public void ExtendSubscription_WhenFreeTier_ThrowsDataIntegrityException()
    {
        // Arrange
        var alertSubscriber = AlertSubscriber.CreateFree(Guid.CreateVersion7());
        var currentUtcNow = UtcNow;

        // Act
        var extendSubscriptionAction = () => alertSubscriber.ExtendSubscription(Guid.CreateVersion7(), Guid.CreateVersion7(), 30, currentUtcNow);

        // Assert
        extendSubscriptionAction.Should()
            .Throw<DataIntegrityException>()
            .WithMessage("*Cannot extend subscription for free tier*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-30)]
    public void ExtendSubscription_WhenInvalidDuration_ThrowsDataIntegrityException(int extensionDurationDays)
    {
        // Arrange
        var alertSubscriber = AlertSubscriber.CreateWithPaidSubscription(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            SubscriptionTier.Pro, 30, UtcNow);
        var currentUtcNow = UtcNow;

        // Act
        var extendSubscriptionAction = () => alertSubscriber.ExtendSubscription(Guid.CreateVersion7(), Guid.CreateVersion7(), extensionDurationDays, currentUtcNow);

        // Assert
        extendSubscriptionAction.Should().Throw<DataIntegrityException>()
            .WithMessage("*Subscription duration must be greater than zero*");
    }

    [Fact]
    public void IsSubscriptionExpired_WhenExpired_ReturnsTrue()
    {
        // Arrange
        // Create subscription that expired 1 day ago (activate from 2 days ago with 1 day duration)
        var pastTime = UtcNow.AddDays(-2);
        var alertSubscriber = AlertSubscriber.CreateWithPaidSubscription(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            SubscriptionTier.Pro, 1, pastTime);

        // Act
        var isSubscriptionExpired = alertSubscriber.IsSubscriptionExpired(UtcNow);

        // Assert
        isSubscriptionExpired.Should().BeTrue();
    }

    [Fact]
    public void IsSubscriptionExpired_WhenActive_ReturnsFalse()
    {
        // Arrange
        var alertSubscriber = AlertSubscriber.CreateWithPaidSubscription(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            SubscriptionTier.Pro, 30, UtcNow);

        // Act
        var isSubscriptionExpired = alertSubscriber.IsSubscriptionExpired(UtcNow);

        // Assert
        isSubscriptionExpired.Should().BeFalse();
    }

    [Fact]
    public void IsSubscriptionExpired_WhenFreeTier_ReturnsFalse()
    {
        // Arrange
        var alertSubscriber = AlertSubscriber.CreateFree(Guid.CreateVersion7());

        // Act
        var isSubscriptionExpired = alertSubscriber.IsSubscriptionExpired(UtcNow);

        // Assert
        isSubscriptionExpired.Should().BeFalse();
    }

    [Fact]
    public void IsSubscriptionExpired_WhenExactlyAtExpiryTime_ReturnsTrue()
    {
        // Arrange - subscription expires exactly at UtcNow
        var alertSubscriber = AlertSubscriber.CreateWithPaidSubscription(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            SubscriptionTier.Pro, 1, UtcNow.AddDays(-1));

        // Act - check at exact expiry moment
        var isSubscriptionExpired = alertSubscriber.IsSubscriptionExpired(UtcNow);

        // Assert - boundary condition: >= means expired at exact moment
        isSubscriptionExpired.Should().BeTrue();
    }

    [Fact]
    public void ActivatePaidSubscription_WhenSameTierRenewal_RaisesReactivatedEvent()
    {
        // Arrange - subscriber had Pro, expired, now renewing Pro again
        var userId = Guid.CreateVersion7();
        var pastTime = UtcNow.AddDays(-10);
        var alertSubscriber =
            AlertSubscriber.CreateWithPaidSubscription(userId, Guid.CreateVersion7(), Guid.CreateVersion7(), SubscriptionTier.Pro, 5,
                pastTime);
        alertSubscriber.DowngradeToFree(UtcNow.AddDays(-5)); // Expired and downgraded
        _ = alertSubscriber.PopDomainEvents(); // Clear events

        // Act - renew with same tier (Pro)
        alertSubscriber.ActivatePaidSubscription(Guid.CreateVersion7(), Guid.CreateVersion7(), SubscriptionTier.Pro, 30, UtcNow);

        // Assert - should be reactivation, not upgrade
        alertSubscriber.SubscriptionTier.Should().Be(SubscriptionTier.Pro);
        alertSubscriber.PopDomainEvents().Should().ContainSingle()
            .Which.Should().BeOfType<SubscriberReactivatedDomainEvent>();
    }

    [Fact]
    public void UpdateTemperatureUnitPreference_WhenCalled_UpdatesPreference()
    {
        // Arrange
        var alertSubscriber = AlertSubscriber.CreateFree(Guid.CreateVersion7());

        // Act
        alertSubscriber.UpdateTemperatureUnitPreference(TemperatureUnit.Fahrenheit);

        // Assert
        alertSubscriber.TemperatureUnitPreference.Should().Be(TemperatureUnit.Fahrenheit);
    }

    [Fact]
    public void CreateWithPaidSubscription_DefaultsToTemperatureUnitCelsius()
    {
        // Arrange
        var userId = Guid.CreateVersion7();

        // Act
        var alertSubscriber = AlertSubscriber.CreateWithPaidSubscription(
            userId, Guid.CreateVersion7(), Guid.CreateVersion7(), SubscriptionTier.Pro, 30, UtcNow);

        // Assert
        alertSubscriber.TemperatureUnitPreference.Should().Be(TemperatureUnit.Celsius);
    }

    [Fact]
    public void CreateFree_DefaultsToWindSpeedUnitKilometersPerHour()
    {
        // Arrange
        var userId = Guid.CreateVersion7();

        // Act
        var alertSubscriber = AlertSubscriber.CreateFree(userId);

        // Assert
        alertSubscriber.WindSpeedUnitPreference.Should().Be(WindSpeedUnit.KilometersPerHour);
    }

    [Fact]
    public void UpdateWindSpeedUnitPreference_WhenCalled_UpdatesPreference()
    {
        // Arrange
        var alertSubscriber = AlertSubscriber.CreateFree(Guid.CreateVersion7());

        // Act
        alertSubscriber.UpdateWindSpeedUnitPreference(WindSpeedUnit.MilesPerHour);

        // Assert
        alertSubscriber.WindSpeedUnitPreference.Should().Be(WindSpeedUnit.MilesPerHour);
    }

    [Fact]
    public void CreateWithPaidSubscription_DefaultsToWindSpeedUnitKilometersPerHour()
    {
        // Arrange
        var userId = Guid.CreateVersion7();

        // Act
        var alertSubscriber = AlertSubscriber.CreateWithPaidSubscription(
            userId, Guid.CreateVersion7(), Guid.CreateVersion7(), SubscriptionTier.Pro, 30, UtcNow);

        // Assert
        alertSubscriber.WindSpeedUnitPreference.Should().Be(WindSpeedUnit.KilometersPerHour);
    }
}
