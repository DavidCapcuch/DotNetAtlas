using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordering.Domain.AlertSubscriptionOrders;

namespace Ordering.Infrastructure.Common.Persistence.Database.Configurations;

public class AlertSubscriptionOrderConfiguration : IEntityTypeConfiguration<AlertSubscriptionOrder>
{
    public void Configure(EntityTypeBuilder<AlertSubscriptionOrder> builder)
    {
        builder.ToTable("SubscriptionOrders", table =>
            table.HasComment("Subscription orders for alert subscription purchases and extensions."));

        builder.HasKey(e => e.Id);

        builder.Property(e => e.UserId)
            .IsRequired();

        builder.Property(e => e.AlertSubscriptionOrderType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(e => e.PaymentMethodId)
            .IsRequired();

        builder.Property(e => e.Tier)
            .HasMaxLength(20);

        builder.Property(e => e.DurationDays)
            .IsRequired();

        builder.Property(e => e.Amount)
            .IsRequired()
            .HasPrecision(19, 4);

        builder.Property(e => e.Currency)
            .IsRequired()
            .HasMaxLength(3)
            .IsUnicode(false);

        builder.Property(e => e.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(255)
            .IsUnicode(false);

        builder.Property(e => e.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(e => e.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(e => e.IdempotencyKey)
            .IsUnique();
    }
}
