using DotNetAtlas.ReliableMessaging.Outbox.EFCore;
using Microsoft.EntityFrameworkCore;
using Ordering.Domain.AlertSubscriptionOrders;

namespace Ordering.Application.Common.Data;

public interface IOrderingDbContext : IOutboxDbContext
{
    DbSet<AlertSubscriptionOrder> AlertSubscriptionOrders { get; }
}
