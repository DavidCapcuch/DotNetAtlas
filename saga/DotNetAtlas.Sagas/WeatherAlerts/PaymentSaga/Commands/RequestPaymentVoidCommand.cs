namespace DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Commands;

/// <summary>
/// Command to void (cancel) an authorized payment.
/// </summary>
public sealed record RequestPaymentVoidCommand
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// User whose payment authorization to void.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Authorization ID from the payment provider.
    /// </summary>
    public string AuthorizationId { get; init; } = string.Empty;

    /// <summary>
    /// Reason for voiding the authorization.
    /// </summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// UTC timestamp when void was requested.
    /// </summary>
    public DateTime RequestedAtUtc { get; init; }
}

