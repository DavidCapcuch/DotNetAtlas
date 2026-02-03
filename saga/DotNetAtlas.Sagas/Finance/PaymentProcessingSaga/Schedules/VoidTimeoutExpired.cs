namespace DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Schedules;

/// <summary>
/// Message sent when the void timeout has expired for a saga instance.
/// </summary>
public sealed record VoidTimeoutExpired
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }
}
