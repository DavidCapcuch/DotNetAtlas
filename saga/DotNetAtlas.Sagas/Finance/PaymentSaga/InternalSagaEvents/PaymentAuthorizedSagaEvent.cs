namespace DotNetAtlas.Sagas.Finance.PaymentSaga.InternalSagaEvents;

/// <summary>
/// Event emitted when payment has been successfully authorized.
/// Funds are reserved but not yet captured.
/// </summary>
public sealed record PaymentAuthorizedSagaEvent
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// User whose payment was authorized.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Authorization ID from the payment provider.
    /// </summary>
    public string AuthorizationId { get; init; } = string.Empty;

    /// <summary>
    /// Authorized amount.
    /// </summary>
    public decimal Amount { get; init; }

    /// <summary>
    /// Currency code.
    /// </summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>
    /// UTC timestamp when authorization was granted.
    /// </summary>
    public DateTime AuthorizedAtUtc { get; init; }

    /// <summary>
    /// UTC timestamp when the authorization expires.
    /// </summary>
    public DateTime ExpiresAtUtc { get; init; }
}
