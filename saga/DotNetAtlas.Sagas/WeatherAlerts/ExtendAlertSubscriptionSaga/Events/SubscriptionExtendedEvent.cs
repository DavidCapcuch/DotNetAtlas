namespace DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Events;

/// <summary>
/// Internal saga event indicating subscription extension has succeeded.
/// Mapped from Weather.Alerts.SubscriptionExtendedEvent by a Kafka consumer.
/// </summary>
public sealed record SubscriptionExtendedEvent
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// User whose subscription was extended.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Payment transaction ID for saga correlation.
    /// </summary>
    public Guid PaymentTransactionId { get; init; }

    /// <summary>
    /// Duration in days that the subscription was extended.
    /// </summary>
    public int DurationExtendedDays { get; init; }

    /// <summary>
    /// New UTC timestamp when the subscription expires.
    /// </summary>
    public DateTime NewExpiresAtUtc { get; init; }

    /// <summary>
    /// UTC timestamp when extension occurred.
    /// </summary>
    public DateTime ExtendedAtUtc { get; init; }
}

