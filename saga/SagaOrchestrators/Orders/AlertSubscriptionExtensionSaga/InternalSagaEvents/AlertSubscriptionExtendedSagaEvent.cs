namespace SagaOrchestrators.Orders.AlertSubscriptionExtensionSaga.InternalSagaEvents;

/// <summary>
/// Internal saga event indicating alert subscription extension has succeeded.
/// Mapped from <c>Weather.Alerts.AlertSubscriptionExtendedEvent</c> by
/// <see cref="Consumers.AlertSubscriptionExtendedConsumer"/>.
/// </summary>
public sealed record AlertSubscriptionExtendedSagaEvent
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// User whose subscription was extended.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Payment transaction ID for saga correlation.
    /// </summary>
    public required Guid PaymentTransactionId { get; init; }

    /// <summary>
    /// Duration in days that the subscription was extended.
    /// </summary>
    public required int DurationExtendedDays { get; init; }

    /// <summary>
    /// New UTC timestamp when the subscription expires.
    /// </summary>
    public required DateTime NewExpiresAtUtc { get; init; }

    /// <summary>
    /// UTC timestamp when extension occurred.
    /// </summary>
    public required DateTime ExtendedAtUtc { get; init; }
}
