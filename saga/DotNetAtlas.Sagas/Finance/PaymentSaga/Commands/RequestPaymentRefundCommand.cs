namespace DotNetAtlas.Sagas.Finance.PaymentSaga.Commands;

/// <summary>
/// Command to request a refund for a captured payment.
/// </summary>
public sealed record RequestPaymentRefundCommand
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// User to refund.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Payment transaction ID to refund.
    /// </summary>
    public Guid PaymentTransactionId { get; init; }

    /// <summary>
    /// Reason for the refund request.
    /// </summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// UTC timestamp when the refund was requested.
    /// </summary>
    public DateTime RequestedAtUtc { get; init; }
}
