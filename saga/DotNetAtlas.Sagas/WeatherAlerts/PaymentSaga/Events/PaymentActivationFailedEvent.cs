namespace DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Events;

/// <summary>
/// Event emitted when subscription activation fails after payment capture.
/// Triggers a refund.
/// </summary>
public sealed record PaymentActivationFailedEvent
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// User whose subscription activation failed.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Payment transaction ID to refund.
    /// </summary>
    public Guid PaymentTransactionId { get; init; }

    /// <summary>
    /// Error code categorizing the failure.
    /// </summary>
    public string ErrorCode { get; init; } = string.Empty;

    /// <summary>
    /// Detailed error message.
    /// </summary>
    public string ErrorMessage { get; init; } = string.Empty;

    /// <summary>
    /// UTC timestamp when the failure occurred.
    /// </summary>
    public DateTime FailedAtUtc { get; init; }

    /// <summary>
    /// Indicates whether this failure should trigger compensation (refund).
    /// </summary>
    public bool ShouldCompensate { get; init; }
}

