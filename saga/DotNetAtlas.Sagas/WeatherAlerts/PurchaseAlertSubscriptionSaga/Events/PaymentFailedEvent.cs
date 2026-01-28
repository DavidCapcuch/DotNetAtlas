namespace DotNetAtlas.Sagas.WeatherAlerts.PurchaseAlertSubscriptionSaga.Events;

/// <summary>
/// Internal saga event indicating payment has failed terminally.
/// Mapped from Finance.Payments.PaymentFailedEvent by a Kafka consumer.
/// </summary>
public sealed record PaymentFailedEvent
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// User whose payment failed.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Error code from the payment provider.
    /// </summary>
    public string ErrorCode { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable error message.
    /// </summary>
    public string ErrorMessage { get; init; } = string.Empty;

    /// <summary>
    /// UTC timestamp when payment failed.
    /// </summary>
    public DateTime FailedAtUtc { get; init; }
}

