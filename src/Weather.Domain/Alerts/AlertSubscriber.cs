using FluentResults;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Exceptions;
using Weather.Domain.Alerts.Entities;
using Weather.Domain.Alerts.Errors;
using Weather.Domain.Alerts.Events;
using Weather.Domain.Alerts.ValueObjects;

namespace Weather.Domain.Alerts;

/// <summary>
/// Aggregate root representing a subscriber with their subscription tier and alert subscriptions.
/// Manages subscription lifecycle including upgrades, downgrades, extensions, and monitored location alert subscriptions.
/// </summary>
/// <remarks>
/// This aggregate can raise the following domain events:
/// <list type="bullet">
/// <item><see cref="SubscriberCreatedDomainEvent"/>: When a new free subscriber is created.</item>
/// <item><see cref="SubscriberActivatedDomainEvent"/>: When a subscriber activates their first paid subscription.</item>
/// <item><see cref="SubscriberReactivatedDomainEvent"/>: When a previously-paid subscriber reactivates their subscription.</item>
/// <item><see cref="SubscriptionUpgradedDomainEvent"/>: When an existing paid subscriber upgrades to a higher tier.</item>
/// <item><see cref="SubscriptionDowngradedDomainEvent"/>: When an expired subscriber is downgraded to Free tier.</item>
/// <item><see cref="SubscriptionExtendedDomainEvent"/>: When a paid subscription is extended.</item>
/// <item><see cref="MonitoredLocationAlertsSubscriptionCreatedDomainEvent"/>: When a user subscribes to alerts for a monitored location.</item>
/// <item><see cref="MonitoredLocationAlertsSubscriptionRemovedDomainEvent"/>: When a user unsubscribes from a monitored location's alerts.</item>
/// </list>
/// </remarks>
public sealed class AlertSubscriber : AggregateRoot<Guid>, IAuditableEntity
{
    public Guid UserId { get; private set; }
    public SubscriptionTier SubscriptionTier { get; private set; }

    /// <summary>
    /// Expiry date for paid subscription. Null for free tier.
    /// </summary>
    public DateTimeOffset? SubscriptionExpiryAtUtc { get; private set; }

    /// <summary>
    /// When the last paid subscription ended (expired or was downgraded).
    /// Null if the subscriber has never had a paid subscription.
    /// Used to distinguish first-time activations from reactivations.
    /// </summary>
    public DateTimeOffset? LastPaidSubscriptionEndedAtUtc { get; private set; }

    /// <summary>
    /// The subscriber's preferred temperature unit for alert notifications.
    /// Defaults to Celsius.
    /// </summary>
    public TemperatureUnit TemperatureUnitPreference { get; private set; } = null!;

    /// <summary>
    /// The subscriber's preferred wind speed unit for alert notifications.
    /// Defaults to kilometers per hour.
    /// </summary>
    public WindSpeedUnit WindSpeedUnitPreference { get; private set; } = null!;

    /// <summary>
    /// Subscriptions to monitored locations (ID reference only).
    /// </summary>
    private readonly List<MonitoredLocationAlertsSubscription> _monitoredLocationSubscriptions = [];

    public IReadOnlyCollection<MonitoredLocationAlertsSubscription> MonitoredLocationAlertsSubscriptions =>
        _monitoredLocationSubscriptions;

    private AlertSubscriber()
    {
    }

    /// <summary>
    /// Creates a new subscriber with the Free tier.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <returns>A new free tier subscriber.</returns>
    /// <remarks>
    /// Possible raised events:
    /// <list type="bullet">
    /// <item><see cref="SubscriberCreatedDomainEvent"/>: Always raised when a new subscriber is created.</item>
    /// </list>
    /// </remarks>
    public static AlertSubscriber CreateFree(Guid userId)
    {
        var subscriber = new AlertSubscriber
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            SubscriptionTier = SubscriptionTier.Free,
            SubscriptionExpiryAtUtc = null,
            LastPaidSubscriptionEndedAtUtc = null,
            TemperatureUnitPreference = TemperatureUnit.Celsius,
            WindSpeedUnitPreference = WindSpeedUnit.KilometersPerHour
        };

        subscriber.AddDomainEvent(new SubscriberCreatedDomainEvent
        {
            SubscriberId = subscriber.Id,
            UserId = userId
        });

        return subscriber;
    }

    /// <summary>
    /// Creates a new subscriber with a paid subscription (Pro/Ultra).
    /// Use this for direct paid purchases where the subscriber doesn't exist yet.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="correlationId">The Correlation ID.</param>
    /// <param name="paymentTransactionId">Payment transaction ID for saga correlation.</param>
    /// <param name="tier">The subscription tier (Pro or Ultra).</param>
    /// <param name="durationDays">Duration of the subscription in days.</param>
    /// <param name="utcNow">Current UTC time for expiry calculation.</param>
    /// <returns>A new paid subscriber.</returns>
    /// <exception cref="DataIntegrityException">Thrown when attempting to create with Free tier or invalid duration.</exception>
    /// <remarks>
    /// Possible raised events:
    /// <list type="bullet">
    /// <item><see cref="SubscriberActivatedDomainEvent"/>: Always raised when a new paid subscriber is created.</item>
    /// </list>
    /// </remarks>
    public static AlertSubscriber CreateWithPaidSubscription(
        Guid userId,
        Guid correlationId,
        Guid paymentTransactionId,
        SubscriptionTier tier,
        int durationDays,
        DateTimeOffset utcNow)
    {
        // We throw exceptions for these validations because these are not expected
        // validation errors but unintended BUGs in the system.
        Throw.If(tier == SubscriptionTier.Free, new DataIntegrityException(
            "Alert.CannotCreatePaidSubscriptionWithFreeTier",
            "Cannot create paid subscription with Free tier. Use CreateFree() instead."));

        Throw.If(durationDays <= 0, new DataIntegrityException(
            "Alert.InvalidSubscriptionDuration",
            "Subscription duration must be greater than zero."));

        var expiryDateUtc = utcNow.AddDays(durationDays);

        var subscriber = new AlertSubscriber
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            SubscriptionTier = tier,
            SubscriptionExpiryAtUtc = expiryDateUtc,
            LastPaidSubscriptionEndedAtUtc = null,
            TemperatureUnitPreference = TemperatureUnit.Celsius,
            WindSpeedUnitPreference = WindSpeedUnit.KilometersPerHour
        };

        subscriber.AddDomainEvent(new SubscriberActivatedDomainEvent
        {
            SubscriberId = subscriber.Id,
            UserId = userId,
            CorrelationId = correlationId,
            PaymentTransactionId = paymentTransactionId,
            Tier = tier,
            DurationDays = durationDays,
            ExpiresAtUtc = expiryDateUtc
        });

        return subscriber;
    }

    public void UpdateTemperatureUnitPreference(TemperatureUnit temperatureUnit) => TemperatureUnitPreference = temperatureUnit;
    public void UpdateWindSpeedUnitPreference(WindSpeedUnit windSpeedUnit) => WindSpeedUnitPreference = windSpeedUnit;

    /// <summary>
    /// Subscribes a user to alerts for a monitored location.
    /// Validates against subscriber's tier limits.
    /// </summary>
    /// <param name="monitoredLocationId">The ID of the monitored location to subscribe to.</param>
    /// <returns>A result indicating success or failure with max subscriptions reached error.</returns>
    /// <remarks>
    /// Possible raised events:
    /// <list type="bullet">
    /// <item><see cref="MonitoredLocationAlertsSubscriptionCreatedDomainEvent"/>: Raised when a new subscription is added (not raised for duplicate subscriptions).</item>
    /// </list>
    /// </remarks>
    public Result SubscribeToMonitoredLocation(Guid monitoredLocationId)
    {
        if (_monitoredLocationSubscriptions.Any(s => s.MonitoredLocationId == monitoredLocationId))
        {
            return Result.Ok();
        }

        if (_monitoredLocationSubscriptions.Count >= SubscriptionTier.MaxSubscriptions)
        {
            return Result.Fail(AlertSubscriberErrors.MaxSubscriptionsReached(SubscriptionTier.MaxSubscriptions));
        }

        var subscription = MonitoredLocationAlertsSubscription.Create(monitoredLocationId);
        _monitoredLocationSubscriptions.Add(subscription);

        AddDomainEvent(new MonitoredLocationAlertsSubscriptionCreatedDomainEvent
        {
            SubscriptionId = subscription.Id,
            MonitoredLocationId = monitoredLocationId,
            UserId = UserId,
            CurrentSubscriptions = _monitoredLocationSubscriptions.Count
        });

        return Result.Ok();
    }

    /// <summary>
    /// Unsubscribes a user from a monitored location's alerts.
    /// </summary>
    /// <param name="monitoredLocationId">The ID of the monitored location to unsubscribe from.</param>
    /// <returns>A result indicating success or failure with not subscribed error.</returns>
    /// <remarks>
    /// Possible raised events:
    /// <list type="bullet">
    /// <item><see cref="MonitoredLocationAlertsSubscriptionRemovedDomainEvent"/>: Raised when a subscription is removed.</item>
    /// </list>
    /// </remarks>
    public Result UnsubscribeFromMonitoredLocation(Guid monitoredLocationId)
    {
        var subscription =
            _monitoredLocationSubscriptions.FirstOrDefault(s => s.MonitoredLocationId == monitoredLocationId);
        if (subscription is null)
        {
            return Result.Ok();
        }

        _monitoredLocationSubscriptions.Remove(subscription);

        AddDomainEvent(new MonitoredLocationAlertsSubscriptionRemovedDomainEvent
        {
            SubscriptionId = subscription.Id,
            MonitoredLocationId = monitoredLocationId,
            UserId = UserId,
            CurrentSubscriptions = _monitoredLocationSubscriptions.Count
        });

        return Result.Ok();
    }

    /// <summary>
    /// Activates or upgrades the subscriber to a paid tier (Pro/Ultra).
    /// Calculates expiry date from the provided duration and current time.
    /// </summary>
    /// <param name="correlationId">The Correlation ID.</param>
    /// <param name="paymentTransactionId">Payment transaction ID for saga correlation.</param>
    /// <param name="newSubscriptionTier">The subscription tier to set (Pro or Ultra).</param>
    /// <param name="durationDays">Duration of the subscription in days.</param>
    /// <param name="utcNow">Current UTC time for expiry calculation.</param>
    /// <exception cref="DataIntegrityException">Thrown when attempting invalid tier transitions or invalid duration.</exception>
    /// <remarks>
    /// Possible raised events:
    /// <list type="bullet">
    /// <item><see cref="SubscriberActivatedDomainEvent"/>: First-time paid subscription (subscriber has never had a paid plan before).</item>
    /// <item><see cref="SubscriberReactivatedDomainEvent"/>: Returning subscriber (previously had paid subscription, currently on Free tier).</item>
    /// <item><see cref="SubscriptionUpgradedDomainEvent"/>: Existing paid subscriber upgrading tier (Pro to Ultra).</item>
    /// </list>
    /// </remarks>
    public void ActivatePaidSubscription(
        Guid correlationId,
        Guid paymentTransactionId,
        SubscriptionTier newSubscriptionTier,
        int durationDays,
        DateTimeOffset utcNow)
    {
        // These are truly exceptional situations that signal a bug, not domain logic errors,
        // which means we throw exceptions instead of returning a Result.Fail()
        Throw.If(newSubscriptionTier == SubscriptionTier.Free, new DataIntegrityException(
            "Alert.CannotUpgradeToFreeTier", "Cannot upgrade to Free tier."));

        Throw.If(SubscriptionTier > newSubscriptionTier, new DataIntegrityException(
            "Alert.CannotDowngradePaidTier",
            $"Cannot downgrade from {SubscriptionTier} to {newSubscriptionTier}. Upgrades only."));

        Throw.If(durationDays <= 0, new DataIntegrityException(
            "Alert.InvalidSubscriptionDuration", "Subscription duration must be greater than zero."));

        var expiryDateUtc = utcNow.AddDays(durationDays);

        var previousTier = SubscriptionTier;
        SubscriptionTier = newSubscriptionTier;
        SubscriptionExpiryAtUtc = expiryDateUtc;

        if (previousTier != SubscriptionTier.Free)
        {
            AddDomainEvent(new SubscriptionUpgradedDomainEvent
            {
                SubscriberId = Id,
                UserId = UserId,
                CorrelationId = correlationId,
                PaymentTransactionId = paymentTransactionId,
                PreviousTier = previousTier,
                NewTier = newSubscriptionTier,
                DurationDays = durationDays,
                ExpiresAtUtc = expiryDateUtc
            });
        }
        else if (LastPaidSubscriptionEndedAtUtc.HasValue)
        {
            AddDomainEvent(new SubscriberReactivatedDomainEvent
            {
                SubscriberId = Id,
                UserId = UserId,
                CorrelationId = correlationId,
                PaymentTransactionId = paymentTransactionId,
                Tier = newSubscriptionTier,
                DurationDays = durationDays,
                ExpiresAtUtc = expiryDateUtc,
                PreviousSubscriptionExpiredAtUtc = LastPaidSubscriptionEndedAtUtc.Value
            });
        }
        else
        {
            AddDomainEvent(new SubscriberActivatedDomainEvent
            {
                SubscriberId = Id,
                UserId = UserId,
                CorrelationId = correlationId,
                PaymentTransactionId = paymentTransactionId,
                Tier = newSubscriptionTier,
                DurationDays = durationDays,
                ExpiresAtUtc = expiryDateUtc
            });
        }
    }

    /// <summary>
    /// Downgrades subscriber to the free tier.
    /// Removes subscriptions exceeding the free tier limit.
    /// Records when the paid subscription ended for future reactivation tracking.
    /// </summary>
    /// <param name="utcNow">Current UTC time for checking if subscription has expired.</param>
    /// <returns>A result indicating success or failure with cannot downgrade active subscription error.</returns>
    /// <remarks>
    /// Possible raised events:
    /// <list type="bullet">
    /// <item><see cref="SubscriptionDowngradedDomainEvent"/>: Raised when subscriber is downgraded from a paid tier to Free (not raised if already on Free tier).</item>
    /// </list>
    /// </remarks>
    public Result DowngradeToFree(DateTimeOffset utcNow)
    {
        if (SubscriptionTier == SubscriptionTier.Free)
        {
            return Result.Ok();
        }

        if (!IsSubscriptionExpired(utcNow))
        {
            return Result.Fail(AlertSubscriberErrors.CannotDowngradeActiveSubscription());
        }

        var previousTier = SubscriptionTier;
        var expiredAt = SubscriptionExpiryAtUtc!.Value;

        SubscriptionTier = SubscriptionTier.Free;
        SubscriptionExpiryAtUtc = null;
        LastPaidSubscriptionEndedAtUtc = expiredAt;

        var maxAllowed = SubscriptionTier.Free.MaxSubscriptions;
        var subscriptionsRemoved = 0;

        if (_monitoredLocationSubscriptions.Count > maxAllowed)
        {
            subscriptionsRemoved = _monitoredLocationSubscriptions.Count - maxAllowed;
            _monitoredLocationSubscriptions.RemoveRange(maxAllowed, subscriptionsRemoved);
        }

        AddDomainEvent(new SubscriptionDowngradedDomainEvent
        {
            SubscriberId = Id,
            UserId = UserId,
            PreviousTier = previousTier,
            ExpiredAtUtc = expiredAt,
            SubscriptionsRemoved = subscriptionsRemoved
        });

        return Result.Ok();
    }

    /// <summary>
    /// Extends the paid subscription (Pro/Ultra) by the specified number of days.
    /// If the subscription has expired, calculates from the current time.
    /// If the subscription is still active, adds days to the current expiry date.
    /// </summary>
    /// <param name="correlationId">The Correlation ID.</param>
    /// <param name="paymentTransactionId">Payment transaction ID for saga correlation.</param>
    /// <param name="durationDays">Number of days to extend the subscription.</param>
    /// <param name="currentUtc">Current UTC time for calculations.</param>
    /// <exception cref="DataIntegrityException">Thrown when extending a free subscription or invalid duration.</exception>
    /// <remarks>
    /// Possible raised events:
    /// <list type="bullet">
    /// <item><see cref="SubscriptionExtendedDomainEvent"/>: Always raised when subscription is extended.</item>
    /// </list>
    /// </remarks>
    public void ExtendSubscription(
        Guid correlationId,
        Guid paymentTransactionId,
        int durationDays,
        DateTimeOffset currentUtc)
    {
        Throw.If(SubscriptionTier == SubscriptionTier.Free, new DataIntegrityException(
            "Alert.CannotExtendFreeSubscription",
            "Cannot extend subscription for free tier user. Payment service bug."));

        Throw.If(durationDays <= 0, new DataIntegrityException(
            "Alert.InvalidSubscriptionDuration",
            "Subscription duration must be greater than zero."));

        // If the subscription has expired or no expiry date, calculate from the current time
        // Otherwise extend from the current expiry date
        var baseDate = SubscriptionExpiryAtUtc.HasValue && SubscriptionExpiryAtUtc.Value > currentUtc
            ? SubscriptionExpiryAtUtc.Value
            : currentUtc;

        SubscriptionExpiryAtUtc = baseDate.AddDays(durationDays);

        AddDomainEvent(new SubscriptionExtendedDomainEvent
        {
            SubscriberId = Id,
            UserId = UserId,
            CorrelationId = correlationId,
            PaymentTransactionId = paymentTransactionId,
            Tier = SubscriptionTier,
            ExtendedByDays = durationDays,
            NewExpiresAtUtc = SubscriptionExpiryAtUtc.Value
        });
    }

    /// <summary>
    /// Checks if the paid subscription has expired.
    /// </summary>
    /// <param name="currentUtc">The current UTC time to check against.</param>
    /// <returns>
    /// <c>true</c> if the subscription is a paid tier and has expired (expiry time is at or before current time);
    /// <c>false</c> for free tier subscriptions or active paid subscriptions.
    /// </returns>
    public bool IsSubscriptionExpired(DateTimeOffset currentUtc)
    {
        return SubscriptionTier != SubscriptionTier.Free
               && SubscriptionExpiryAtUtc.HasValue
               && currentUtc >= SubscriptionExpiryAtUtc.Value;
    }

    public DateTimeOffset CreatedUtc { get; private set; }
    public DateTimeOffset LastModifiedUtc { get; private set; }
}
