namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.InternalSagaEvents;

/// <summary>
/// Internal saga event indicating an alert subscription extension has been initiated.
/// Mapped from <c>Order.AlertSubscriptions.AlertSubscriptionExtensionInitiatedEvent</c> by
/// <see cref="Consumers.AlertSubscriptionExtensionInitiatedConsumer"/>.
/// </summary>
public sealed record AlertSubscriptionExtensionInitiatedSagaEvent
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// User initiating the subscription extension.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// ID of the saved payment method to use.
    /// </summary>
    public required Guid PaymentMethodId { get; init; }

    /// <summary>
    /// Duration to extend the subscription in days.
    /// </summary>
    public required int DurationDays { get; init; }

    /// <summary>
    /// Payment amount for the extension.
    /// </summary>
    public required decimal Amount { get; init; }

    /// <summary>
    /// ISO 4217 currency code.
    /// </summary>
    public required string Currency { get; init; }

    /// <summary>
    /// Idempotency key for preventing duplicate extensions.
    /// </summary>
    public required string IdempotencyKey { get; init; }

    /// <summary>
    /// UTC timestamp when extension was initiated.
    /// </summary>
    public required DateTime InitiatedAtUtc { get; init; }
}
