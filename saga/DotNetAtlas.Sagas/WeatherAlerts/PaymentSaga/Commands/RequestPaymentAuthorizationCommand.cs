namespace DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Commands;

/// <summary>
/// Command to request payment authorization from the payment service.
/// </summary>
public sealed record RequestPaymentAuthorizationCommand
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// User to authorize payment for.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// ID of the saved payment method to use.
    /// </summary>
    public Guid PaymentMethodId { get; init; }

    /// <summary>
    /// Amount to authorize.
    /// </summary>
    public decimal Amount { get; init; }

    /// <summary>
    /// ISO 4217 currency code.
    /// </summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>
    /// Idempotency key to prevent duplicate authorizations.
    /// </summary>
    public string IdempotencyKey { get; init; } = string.Empty;

    /// <summary>
    /// UTC timestamp when authorization was requested.
    /// </summary>
    public DateTime RequestedAtUtc { get; init; }
}

