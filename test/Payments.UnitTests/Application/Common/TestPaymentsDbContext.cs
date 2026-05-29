using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Payments.Application.Common.Data;
using Payments.Domain.Transactions;
using Platform.ReliableMessaging.Outbox.Core;

namespace Payments.UnitTests.Application.Common;

/// <summary>
/// Minimal test-only implementation of <see cref="IPaymentsDbContext"/> over EF Core InMemory.
/// The production <c>PaymentsDbContext</c> (with PII <c>_enc</c> columns, SmartEnum conversions,
/// owned-value-object mappings) lives in the Infrastructure layer.
/// </summary>
/// <remarks>
/// EF Core InMemory resolves tracking queries through the identity map, returning the very
/// instance that was seeded — so the Ignored complex properties (Money, SmartEnum, owned VOs)
/// remain live CLR references on the returned aggregate, which is all the command-handler tests
/// need. Read-side projections that materialise those VOs (the <c>Get*QueryHandler</c>s, which
/// use <c>AsNoTracking</c>) are tested at the integration tier against real Postgres instead.
/// </remarks>
public sealed class TestPaymentsDbContext : DbContext, IPaymentsDbContext
{
    public TestPaymentsDbContext(DbContextOptions<TestPaymentsDbContext> options)
        : base(options)
    {
    }

    public DbSet<PaymentTransaction> Transactions { get; set; } = null!;

    public DbSet<OutboxMessage> OutboxMessages { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PaymentTransaction>(ConfigurePaymentTransaction);
        modelBuilder.Entity<OutboxMessage>().HasKey(o => o.Id);
    }

    private static void ConfigurePaymentTransaction(EntityTypeBuilder<PaymentTransaction> builder)
    {
        builder.HasKey(t => t.Id);

        // Complex VO / SmartEnum / owned-value-object properties are ignored at the EF mapping
        // level. InMemory keeps them as live CLR references on the tracked entity, which is
        // enough for the write-side handler tests (they query by Id / CorrelationId — both mapped
        // scalars — and assert on the tracked instance). The production DbContext configures
        // these fully (owned Money, PII _enc columns per ADR-0011, SmartEnum conversions).
        builder.Ignore(t => t.Amount);
        builder.Ignore(t => t.PaymentMethodId);
        builder.Ignore(t => t.Status);
        builder.Ignore(t => t.GatewayResponseCode);
        builder.Ignore(t => t.FailureInfo);
    }
}
