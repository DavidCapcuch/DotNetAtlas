using Platform.ReliableMessaging.Outbox.EFCore;

namespace Payments.Common.Persistence.Database;

public interface IPaymentDbContext : IOutboxDbContext
{
}
