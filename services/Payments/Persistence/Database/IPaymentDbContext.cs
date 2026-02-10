using DotNetAtlas.ReliableMessaging.Outbox.EFCore;

namespace Payments.Persistence.Database;

public interface IPaymentDbContext : IOutboxDbContext
{
}
