namespace DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Events;

/// <summary>
/// Internal saga event indicating subscription extension has failed.
/// Mapped from Weather.Alerts.SubscriptionExtensionFailedEvent by a Kafka consumer.
/// </summary>
public sealed record SubscriptionExtensionFailedEvent
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// User whose subscription extension failed.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Error code for categorized failure handling.
    /// </summary>
    public string ErrorCode { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable error message.
    /// </summary>
    public string ErrorMessage { get; init; } = string.Empty;

    /// <summary>
    /// Whether compensation (refund) should be triggered.
    /// </summary>
    public bool ShouldCompensate { get; init; }

    /// <summary>
    /// UTC timestamp when extension failed.
    /// </summary>
    public DateTime FailedAtUtc { get; init; }
}

