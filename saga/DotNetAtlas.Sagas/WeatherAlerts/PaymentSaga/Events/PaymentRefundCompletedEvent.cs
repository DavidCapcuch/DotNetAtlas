namespace DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Events;

/// <summary>
/// Event emitted when a refund for a captured payment has been completed.
/// </summary>
public sealed record PaymentRefundCompletedEvent
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// User who received the refund.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// The payment transaction ID that was refunded.
    /// </summary>
    public Guid PaymentTransactionId { get; init; }

    /// <summary>
    /// The refund transaction ID.
    /// </summary>
    public Guid RefundTransactionId { get; init; }

    /// <summary>
    /// UTC timestamp when refund was completed.
    /// </summary>
    public DateTime RefundedAtUtc { get; init; }
}

