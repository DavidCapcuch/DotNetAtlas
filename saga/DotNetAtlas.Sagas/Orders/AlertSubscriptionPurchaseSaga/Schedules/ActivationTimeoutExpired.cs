namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Schedules;

/// <summary>
/// Message sent when the activation timeout has expired for a saga instance.
/// </summary>
public sealed record ActivationTimeoutExpired
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }
}
