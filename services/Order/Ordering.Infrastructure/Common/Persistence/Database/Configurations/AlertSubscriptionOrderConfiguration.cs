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
            .HasConversion<string?>()
            .HasMaxLength(20);

        builder.Property(e => e.DurationDays)
            .IsRequired();

        builder.OwnsOne(e => e.Price, priceBuilder =>
        {
            priceBuilder.Property(m => m.Amount)
                .HasColumnName("Amount")
                .HasPrecision(19, 4)
                .IsRequired();

            priceBuilder.Property(m => m.Currency)
                .HasColumnName("Currency")
                .HasConversion<string>()
                .HasMaxLength(3)
                .IsUnicode(false)
                .IsRequired();
        });

        builder.Navigation(e => e.Price)
            .IsRequired();

        builder.Property(e => e.Status)
            .IsRequired()
            .HasConversion(
                s => s.Name,
                v => AlertSubscriptionOrderStatus.FromName(v))
            .HasMaxLength(30);

        builder.Property(e => e.CreatedAtUtc)
            .IsRequired();
    }
}
