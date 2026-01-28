namespace DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Events;

/// <summary>
/// Event emitted when payment authorization fails.
/// </summary>
public sealed record PaymentAuthorizationFailedEvent
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// User whose payment authorization failed.
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
    /// Indicates whether this failure is retryable.
    /// </summary>
    public bool IsRetryable { get; init; }

    /// <summary>
    /// UTC timestamp when authorization failed.
    /// </summary>
    public DateTime FailedAtUtc { get; init; }
}

