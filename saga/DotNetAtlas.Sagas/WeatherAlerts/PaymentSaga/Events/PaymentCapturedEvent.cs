namespace DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Events;

/// <summary>
/// Event emitted when payment has been successfully captured.
/// Funds have been transferred.
/// </summary>
public sealed record PaymentCapturedEvent
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// User whose payment was captured.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Payment transaction ID for the captured funds.
    /// </summary>
    public Guid PaymentTransactionId { get; init; }

    /// <summary>
    /// Authorization ID that was captured.
    /// </summary>
    public string AuthorizationId { get; init; } = string.Empty;

    /// <summary>
    /// Captured amount.
    /// </summary>
    public decimal Amount { get; init; }

    /// <summary>
    /// Currency code.
    /// </summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>
    /// UTC timestamp when capture was completed.
    /// </summary>
    public DateTime CapturedAtUtc { get; init; }
}

