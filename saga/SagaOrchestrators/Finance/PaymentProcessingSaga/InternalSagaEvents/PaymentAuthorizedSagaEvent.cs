namespace SagaOrchestrators.Finance.PaymentProcessingSaga.InternalSagaEvents;

/// <summary>
/// Event emitted when payment has been successfully authorized.
/// Funds are reserved but not yet captured.
/// </summary>
public sealed record PaymentAuthorizedSagaEvent
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// User whose payment was authorized.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Authorization ID from the payment provider.
    /// </summary>
    public required string AuthorizationId { get; init; }

    /// <summary>
    /// Authorized amount.
    /// </summary>
    public required decimal Amount { get; init; }

    /// <summary>
    /// Currency code.
    /// </summary>
    public required string Currency { get; init; }

    /// <summary>
    /// UTC timestamp when authorization was granted.
    /// </summary>
    public required DateTime AuthorizedAtUtc { get; init; }

    /// <summary>
    /// UTC timestamp when the authorization expires.
    /// </summary>
    public required DateTime ExpiresAtUtc { get; init; }
}
