using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordering.Application.Common.Data;
using Ordering.Domain.Orders;
using Platform.ReliableMessaging.Outbox.Core;

namespace Ordering.UnitTests.Application.Common;

/// <summary>
/// Minimal test-only implementation of <see cref="IOrderingDbContext"/> over
/// EF Core InMemory. The production <c>OrderingDbContext</c> (with PII
/// <c>_enc</c> columns, SmartEnum conversions, value-object OwnsOne mappings)
/// lives in the Infrastructure layer (M4).
/// </summary>
/// <remarks>
/// EF Core InMemory stores tracked entities by reference, so we do not need
/// to configure SmartEnums, value-objects, or owned types to load/save.
/// Complex properties are ignored at the EF mapping level — the in-memory
/// aggregate state is still directly accessible on the tracked object,
/// which is all that handler tests need. LINQ against <see cref="Order.Status"/>
/// still works because the InMemory provider evaluates client-side against
/// the live CLR object.
/// </remarks>
public sealed class TestOrderingDbContext : DbContext, IOrderingDbContext
{
    public TestOrderingDbContext(DbContextOptions<TestOrderingDbContext> options)
        : base(options)
    {
    }

    public DbSet<Order> Orders { get; set; } = null!;

    public DbSet<OutboxMessage> OutboxMessages { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Order>(ConfigureOrder);
        modelBuilder.Entity<OutboxMessage>().HasKey(o => o.Id);
    }

    private static void ConfigureOrder(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(o => o.Id);

        // Complex VO / SmartEnum / owned-type properties are ignored at the
        // EF mapping level. InMemory keeps them as live CLR references on the
        // tracked entity, which is enough for handler tests. The production
        // DbContext in M4 configures them fully with PII column naming per
        // ADR-0011.
        builder.Ignore(o => o.Total);
        builder.Ignore(o => o.ShippingAddress);
        builder.Ignore(o => o.BillingAddress);
        builder.Ignore(o => o.Items);
        builder.Ignore(o => o.Cancellation);
        builder.Ignore(o => o.Failure);
        builder.Ignore(o => o.Shipment);
        builder.Ignore(o => o.Status);
    }
}
