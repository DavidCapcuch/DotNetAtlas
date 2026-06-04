namespace Notifications.Domain.Deliveries;

/// <summary>
/// Outcome recorded in the per-channel <see cref="NotificationDelivery"/> ledger. A first send
/// attempt records <see cref="Dispatched"/> or <see cref="Failed"/>; a later retry of a
/// <see cref="Failed"/> row flips it to <see cref="Dispatched"/> (never a second insert).
/// </summary>
public enum DeliveryStatus
{
    /// <summary>The channel send succeeded.</summary>
    Dispatched,

    /// <summary>The channel send failed; eligible to retry (may flip to <see cref="Dispatched"/>).</summary>
    Failed,
}
