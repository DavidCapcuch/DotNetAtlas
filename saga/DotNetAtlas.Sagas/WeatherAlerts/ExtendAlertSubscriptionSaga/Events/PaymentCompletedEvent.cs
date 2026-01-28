namespace DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Events;

/// <summary>
/// Internal saga event indicating payment has been completed successfully.
/// Mapped from Finance.Payments.PaymentCompletedEvent by a Kafka consumer.
/// </summary>
public sealed record PaymentCompletedEvent
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// User whose payment was completed.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Payment transaction ID. Used for refunds if extension fails.
    /// </summary>
    public Guid PaymentTransactionId { get; init; }

    /// <summary>
    /// Amount that was charged.
    /// </summary>
    public decimal Amount { get; init; }

    /// <summary>
    /// ISO 4217 currency code.
    /// </summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>
    /// UTC timestamp when payment was completed.
    /// </summary>
    public DateTime CompletedAtUtc { get; init; }
}

