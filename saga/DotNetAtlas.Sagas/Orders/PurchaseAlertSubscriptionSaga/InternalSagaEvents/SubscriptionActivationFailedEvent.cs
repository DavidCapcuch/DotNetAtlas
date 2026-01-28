namespace DotNetAtlas.Sagas.Orders.PurchaseAlertSubscriptionSaga.InternalSagaEvents;

/// <summary>
/// Event emitted when a subscription activation has failed.
/// </summary>
public sealed record SubscriptionActivationFailedEvent
{
    /// <summary>
    /// Correlation ID to link with the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// Identifier of the user whose subscription activation failed.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// The payment transaction ID for potential refund.
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
