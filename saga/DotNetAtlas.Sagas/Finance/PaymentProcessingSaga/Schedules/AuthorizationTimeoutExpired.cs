namespace DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Schedules;

/// <summary>
/// Message sent when the authorization timeout has expired for a saga instance.
/// </summary>
public sealed record AuthorizationTimeoutExpired
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }
}
