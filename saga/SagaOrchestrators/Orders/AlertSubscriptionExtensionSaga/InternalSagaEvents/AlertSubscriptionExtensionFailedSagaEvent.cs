namespace SagaOrchestrators.Orders.AlertSubscriptionExtensionSaga.InternalSagaEvents;

/// <summary>
/// Internal saga event indicating alert subscription extension has failed.
/// Mapped from <c>Weather.Alerts.AlertSubscriptionExtensionFailedEvent</c> by a Kafka consumer.
/// </summary>
public sealed record AlertSubscriptionExtensionFailedSagaEvent
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// User whose subscription extension failed.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Error code for categorized failure handling.
    /// </summary>
    public required string ErrorCode { get; init; }

    /// <summary>
    /// Human-readable error message.
    /// </summary>
    public required string ErrorMessage { get; init; }

    /// <summary>
    /// Whether compensation (refund) should be triggered.
    /// </summary>
    public required bool ShouldCompensate { get; init; }

    /// <summary>
    /// UTC timestamp when extension failed.
    /// </summary>
    public required DateTime FailedAtUtc { get; init; }
}
