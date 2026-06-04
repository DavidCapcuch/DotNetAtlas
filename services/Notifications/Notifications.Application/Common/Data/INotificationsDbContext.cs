using Microsoft.EntityFrameworkCore;
using Notifications.Domain.Deliveries;
using Platform.ReliableMessaging.Outbox.EFCore;

namespace Notifications.Application.Common.Data;

/// <summary>
/// The Notifications write model exposed to the Application layer: the platform outbox plus the
/// per-channel delivery ledger (<see cref="NotificationDeliveries"/>). v1 was an empty marker
/// (no domain tables); v2 (#312) adds the ledger.
/// </summary>
public interface INotificationsDbContext : IOutboxDbContext
{
    /// <summary>Per-channel delivery ledger, keyed (<c>NotificationId</c>, <c>Channel</c>). See ADR-0031/0032.</summary>
    DbSet<NotificationDelivery> NotificationDeliveries { get; }
}
