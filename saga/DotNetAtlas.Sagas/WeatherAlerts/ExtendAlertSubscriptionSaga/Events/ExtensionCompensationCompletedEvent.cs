namespace DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Events;

/// <summary>
/// Internal saga event indicating compensation (refund) has been completed.
/// Mapped from Finance.Payments.PaymentRefundedEvent by a Kafka consumer.
/// </summary>
public sealed record ExtensionCompensationCompletedEvent
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// User whose payment was refunded.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Refund transaction ID.
    /// </summary>
    public Guid RefundTransactionId { get; init; }

    /// <summary>
    /// UTC timestamp when compensation was completed.
    /// </summary>
    public DateTime CompensatedAtUtc { get; init; }
}

