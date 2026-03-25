namespace SagaOrchestrators.Orders.AlertSubscriptionPurchaseSaga.Schedules;

/// <summary>
/// Message sent when the payment timeout has expired for a saga instance.
/// </summary>
public sealed record PaymentTimeoutExpired
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }
}
