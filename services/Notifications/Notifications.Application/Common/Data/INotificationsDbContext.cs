using Microsoft.EntityFrameworkCore;
using Notifications.Domain.Deliveries;
using Notifications.Domain.Preferences;
using Notifications.Domain.Templates;
using Platform.ReliableMessaging.Outbox.EFCore;

namespace Notifications.Application.Common.Data;

/// <summary>
/// The Notifications write model exposed to the Application layer: the platform outbox, the
/// per-channel delivery ledger (<see cref="NotificationDeliveries"/>), the seeded template
/// reference tables (<see cref="Templates"/> / <see cref="TemplateChannels"/>), and the seeded
/// recipient preference reference table (<see cref="UserPreferences"/>). v1 was an empty marker
/// (no domain tables); v2 adds the ledger (#312), templates (#313), and preferences (#314).
/// </summary>
public interface INotificationsDbContext : IOutboxDbContext
{
    /// <summary>Per-channel delivery ledger, keyed (<c>NotificationId</c>, <c>Channel</c>). See ADR-0031/0032.</summary>
    DbSet<NotificationDelivery> NotificationDeliveries { get; }

    /// <summary>Seeded notification templates, keyed <c>TemplateKey</c>. See ADR-0032 § 7.</summary>
    DbSet<Template> Templates { get; }

    /// <summary>Per-channel template content + supported-channel set, keyed (<c>TemplateKey</c>, <c>Channel</c>). See ADR-0032 § 7.</summary>
    DbSet<TemplateChannel> TemplateChannels { get; }

    /// <summary>Seeded recipient preference + contact reference, keyed <c>UserId</c> (Keycloak sub). See notifications.md § 8.</summary>
    DbSet<NotificationPreference> UserPreferences { get; }
}
