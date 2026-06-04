using Notifications.Domain.Channels;

namespace Notifications.Domain.Deliveries;

/// <summary>
/// Per-channel delivery ledger row — the idempotency + audit record that makes a durable
/// channel at-most-once across Hangfire retries and Kafka redelivery. Keyed
/// (<see cref="NotificationId"/>, <see cref="Channel"/>) with a unique index; the channel
/// dispatcher UPSERTs it as it sends. Not an aggregate root — a guarded idempotency record,
/// not an invariant-protected object graph. See ADR-0031 (idempotency) and ADR-0032 § 2.
/// </summary>
public sealed class NotificationDelivery
{
    private NotificationDelivery(
        Guid notificationId,
        ChannelType channel,
        DeliveryStatus status,
        DateTimeOffset nowUtc)
    {
        NotificationId = notificationId;
        Channel = channel;
        Status = status;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    // EF Core materialisation constructor.
    private NotificationDelivery()
    {
    }

    /// <summary>Producer-assigned notification intent identity (half of the ledger key).</summary>
    public Guid NotificationId { get; private set; }

    /// <summary>Delivery channel this row records (the other half of the ledger key).</summary>
    public ChannelType Channel { get; private set; } = null!;

    /// <summary>Latest recorded outcome for (<see cref="NotificationId"/>, <see cref="Channel"/>).</summary>
    public DeliveryStatus Status { get; private set; }

    /// <summary>UTC time the row was first inserted.</summary>
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>UTC time of the latest status write.</summary>
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary><c>true</c> when the send already succeeded — the dispatcher skips re-sending.</summary>
    public bool IsDispatched => Status == DeliveryStatus.Dispatched;

    /// <summary>Records a first delivery attempt (INSERT) with the given outcome.</summary>
    public static NotificationDelivery Record(
        Guid notificationId,
        ChannelType channel,
        DeliveryStatus status,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return new NotificationDelivery(notificationId, channel, status, nowUtc);
    }

    /// <summary>Flips a previously-<see cref="DeliveryStatus.Failed"/> row to <see cref="DeliveryStatus.Dispatched"/> (UPDATE).</summary>
    public void MarkDispatched(DateTimeOffset nowUtc)
    {
        Status = DeliveryStatus.Dispatched;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Records a repeated failed attempt (UPDATE; keeps the row <see cref="DeliveryStatus.Failed"/>).</summary>
    public void MarkFailed(DateTimeOffset nowUtc)
    {
        Status = DeliveryStatus.Failed;
        UpdatedAtUtc = nowUtc;
    }
}
