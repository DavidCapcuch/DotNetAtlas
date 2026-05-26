using Platform.ReliableMessaging.Outbox.EFCore;

namespace Notifications.Application.Common.Data;

public interface INotificationsDbContext : IOutboxDbContext
{
}
