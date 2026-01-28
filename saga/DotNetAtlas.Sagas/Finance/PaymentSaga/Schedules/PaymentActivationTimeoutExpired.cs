namespace DotNetAtlas.Sagas.Finance.PaymentSaga.Schedules;

/// <summary>
/// Message sent when the activation timeout has expired for a payment saga instance.
/// </summary>
public sealed record PaymentActivationTimeoutExpired
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }
}
