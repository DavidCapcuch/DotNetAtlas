using Platform.SharedKernel.Base.DomainEvents;

namespace Weather.Domain.Alerts.Events;

/// <summary>
/// Domain event raised when a new free subscriber is created.
/// This represents a user signing up for the service without a paid subscription.
/// </summary>
public sealed record SubscriberCreatedDomainEvent : DomainEvent
{
    /// <summary>
    /// Identifier of the subscriber aggregate.
    /// </summary>
    public required Guid SubscriberId { get; init; }

    /// <summary>
    /// User identifier.
    /// </summary>
    public required Guid UserId { get; init; }
}
