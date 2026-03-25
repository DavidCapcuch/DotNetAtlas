namespace SagaOrchestrators.Orders.AlertSubscriptionExtensionSaga.Schedules;

/// <summary>
/// Schedule message indicating compensation timeout has expired.
/// </summary>
public sealed record CompensationTimeoutExpired
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }
}
