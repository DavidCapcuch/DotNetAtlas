using DotNetAtlas.ReliableMessaging.Outbox.EFCore;

namespace Notifications.Common.Persistence.Database;

public interface INotificationDbContext : IOutboxDbContext
{
}
