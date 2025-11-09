using DotNetAtlas.Outbox.Core;
using Microsoft.EntityFrameworkCore;

namespace DotNetAtlas.Outbox.EntityFrameworkCore.EntityFramework;

/// <summary>
/// Contract for DbContext that supports the outbox pattern.
/// </summary>
public interface IOutboxDbContext
{
    DbSet<OutboxMessage> OutboxMessages { get; }
}
