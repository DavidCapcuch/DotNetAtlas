using Microsoft.EntityFrameworkCore;
using Ordering.Domain.AlertSubscriptionOrders;
using Platform.ReliableMessaging.Outbox.EFCore;

namespace Ordering.Application.Common.Data;

public interface IOrderingDbContext : IOutboxDbContext
{
    DbSet<AlertSubscriptionOrder> AlertSubscriptionOrders { get; }
}
