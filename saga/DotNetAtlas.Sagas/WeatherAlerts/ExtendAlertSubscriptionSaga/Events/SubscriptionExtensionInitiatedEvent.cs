namespace DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Events;

/// <summary>
/// Internal saga event indicating a subscription extension has been initiated.
/// Mapped from Order.AlertSubscriptions.AlertSubscriptionExtensionInitiatedEvent by a Kafka consumer.
/// </summary>
public sealed record SubscriptionExtensionInitiatedEvent
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// User initiating the subscription extension.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// ID of the saved payment method to use.
    /// </summary>
    public Guid PaymentMethodId { get; init; }

    /// <summary>
    /// Duration to extend the subscription in days.
    /// </summary>
    public int DurationDays { get; init; }

    /// <summary>
    /// Payment amount for the extension.
    /// </summary>
    public decimal Amount { get; init; }

    /// <summary>
    /// ISO 4217 currency code.
    /// </summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>
    /// Idempotency key for preventing duplicate extensions.
    /// </summary>
    public string IdempotencyKey { get; init; } = string.Empty;

    /// <summary>
    /// UTC timestamp when extension was initiated.
    /// </summary>
    public DateTime InitiatedAtUtc { get; init; }
}

