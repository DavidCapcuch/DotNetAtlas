namespace DotNetAtlas.Sagas.Finance.PaymentSaga.Commands;

/// <summary>
/// Command to capture an authorized payment.
/// </summary>
public sealed record RequestPaymentCaptureCommand
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// User whose payment to capture.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Authorization ID from the payment provider.
    /// </summary>
    public string AuthorizationId { get; init; } = string.Empty;

    /// <summary>
    /// Amount to capture.
    /// </summary>
    public decimal Amount { get; init; }

    /// <summary>
    /// UTC timestamp when capture was requested.
    /// </summary>
    public DateTime RequestedAtUtc { get; init; }
}
