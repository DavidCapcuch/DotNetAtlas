using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordering.Domain.Orders;
using Ordering.Domain.Orders.ValueObjects;
using Platform.SharedKernel.ValueObjects;

namespace Ordering.Infrastructure.Persistence.Database.EntityConfigurations.Orders;

/// <summary>
/// EF Core mapping for the <see cref="Order"/> aggregate root. Applies:
/// <list type="bullet">
/// <item>Postgres <c>xmin</c> system column as optimistic concurrency token
/// via the inherited <c>Entity.RowVersion</c> property + <c>.IsRowVersion()</c>
/// (Appendix B.3 — Npgsql 10's convention maps uint + rowVersion to xmin,
/// matching the Weather / codebase-wide pattern).</item>
/// <item>PII <c>*_enc</c> column naming on flattened <c>Address</c> owned types
/// per ADR-0011 (v1 plaintext; v2 encrypts per-buyer DEK).</item>
/// <item>Owned <see cref="Money"/>, <see cref="OrderItem"/>, and status-info VOs.</item>
/// </list>
/// </summary>
internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders", t => t.HasComment(
            "Order aggregate — lifecycle from creation through delivery/cancellation/failure."));

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id)
            .ValueGeneratedNever()
            .HasComment("Primary key (Guid v7 — time-ordered).");

        // Optimistic concurrency via Postgres xmin system column (Appendix B.3).
        // `Entity.RowVersion` is inherited from Platform.SharedKernel; Npgsql's
        // RowVersion convention maps it to the xmin system column (no stored
        // column). Matches the Weather reference mapping.
        builder.Property(o => o.RowVersion)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasComment("Optimistic concurrency token (Postgres xmin system column).");

        builder.Property(o => o.BuyerId)
            .HasComment("JWT sub of the buyer who placed the order.");
        builder.HasIndex(o => o.BuyerId).HasDatabaseName("IX_Orders_BuyerId");

        builder.Property(o => o.CorrelationId)
            .HasComment("Checkout saga correlation id. Idempotency key for CreateOrderCommand.");
        builder.HasIndex(o => o.CorrelationId)
            .IsUnique()
            .HasDatabaseName("UX_Orders_CorrelationId");

        builder.Property(o => o.PaymentMethodId)
            .HasComment("Payments-side payment method reference.");

        builder.Property(o => o.PaymentTransactionId)
            .HasComment("Payments-side transaction id after MarkPaymentCompleted (nullable pre-payment).");

        builder.Property(o => o.StockReservationId)
            .HasComment("Inventory-side reservation id after MarkStockReserved (nullable pre-reservation).");

        builder.Property(o => o.Status)
            .HasComment("Lifecycle status (Created..Delivered + Cancelled/Failed off-ramps).")
            .HasConversion(
                status => status.Value,
                value => OrderStatus.FromValue(value));

        builder.Property(o => o.CreatedAtUtc)
            .HasComment("UTC timestamp when the order was created (business time, frozen).");
        builder.Property(o => o.StockReservedAtUtc)
            .HasComment("UTC timestamp when stock was reserved (nullable).");
        builder.Property(o => o.PaymentCompletedAtUtc)
            .HasComment("UTC timestamp when payment was completed (nullable).");
        builder.Property(o => o.ConfirmedAtUtc)
            .HasComment("UTC timestamp when the order was confirmed (nullable).");
        builder.Property(o => o.DeliveredAtUtc)
            .HasComment("UTC timestamp when the order was delivered (nullable).");

        builder.Property(o => o.CreatedUtc)
            .HasComment("Row-level audit: created timestamp (UTC). Set by interceptor.");
        builder.Property(o => o.LastModifiedUtc)
            .HasComment("Row-level audit: last-modified timestamp (UTC). Set by interceptor.");

        builder.HasIndex(o => new { o.BuyerId, o.CreatedAtUtc })
            .HasDatabaseName("IX_Orders_BuyerId_CreatedAtUtc");

        // Total as owned Money (flat amount + currency).
        builder.OwnsOne(o => o.Total, total =>
        {
            total.Property(m => m.Amount)
                .HasColumnName("total_amount")
                .HasPrecision(19, 4)
                .HasComment("Order total amount (sum of line totals).");
            total.Property(m => m.Currency)
                .HasColumnName("total_currency")
                .HasMaxLength(3)
                .HasComment("ISO 4217 currency code (uniform across all items, invariant I-9).")
                .HasConversion(
                    c => c.Name,
                    name => CurrencyCode.FromName(name, ignoreCase: false));
        });

        // ShippingAddress owned entity — PII columns suffixed _enc per ADR-0011.
        // V1 stores plaintext; the suffix reserves the contract for v2 per-buyer DEK encryption.
        builder.OwnsOne(o => o.ShippingAddress, ConfigureAddress("shipping_address"));

        // BillingAddress — identical shape, independent flattened columns.
        builder.OwnsOne(o => o.BillingAddress, ConfigureAddress("billing_address"));
        builder.Navigation(o => o.BillingAddress).IsRequired();
        builder.Navigation(o => o.ShippingAddress).IsRequired();

        // Cancellation / Failure / Shipment — optional owned VOs.
        builder.OwnsOne(o => o.Cancellation, cancellation =>
        {
            cancellation.Property(c => c.Reason)
                .HasColumnName("cancellation_reason")
                .HasMaxLength(CancellationInfo.MaxReasonLength)
                .HasComment("Cancellation reason (<=500 chars).");
            cancellation.Property(c => c.AtStatus)
                .HasColumnName("cancellation_at_status")
                .HasComment("Status the order was in when cancelled.")
                .HasConversion(
                    status => status.Value,
                    value => OrderStatus.FromValue(value));
            cancellation.Property(c => c.CancelledAtUtc)
                .HasColumnName("cancelled_at_utc")
                .HasComment("UTC timestamp when the order was cancelled.");
        });

        builder.OwnsOne(o => o.Failure, failure =>
        {
            failure.Property(f => f.ErrorCode)
                .HasColumnName("failure_error_code")
                .HasMaxLength(FailureInfo.MaxErrorCodeLength)
                .HasComment("Machine-readable error code at failure time.");
            failure.Property(f => f.ErrorMessage)
                .HasColumnName("failure_error_message")
                .HasMaxLength(FailureInfo.MaxErrorMessageLength)
                .HasComment("Human-readable error message at failure time.");
            failure.Property(f => f.AtStatus)
                .HasColumnName("failure_at_status")
                .HasComment("Status the order was in when it failed.")
                .HasConversion(
                    status => status.Value,
                    value => OrderStatus.FromValue(value));
            failure.Property(f => f.FailedAtUtc)
                .HasColumnName("failed_at_utc")
                .HasComment("UTC timestamp when the order was marked Failed.");
        });

        builder.OwnsOne(o => o.Shipment, shipment =>
        {
            shipment.Property(s => s.Carrier)
                .HasColumnName("shipment_carrier")
                .HasMaxLength(ShipmentInfo.MaxCarrierLength)
                .HasComment("Shipping carrier identifier.");
            shipment.Property(s => s.TrackingNumber)
                .HasColumnName("shipment_tracking_number")
                .HasMaxLength(ShipmentInfo.MaxTrackingNumberLength)
                .HasComment("Carrier-assigned tracking number.");
            shipment.Property(s => s.ShippedAtUtc)
                .HasColumnName("shipped_at_utc")
                .HasComment("UTC timestamp when the order shipped.");
        });

        // Items owned collection — separate table with shadow FK to Orders.Id.
        builder.OwnsMany(o => o.Items, items =>
        {
            items.ToTable("order_items", t => t.HasComment(
                "Order line items — value-object collection, no independent lifecycle."));
            items.WithOwner().HasForeignKey("OrderId");
            items.Property<int>("Ordinal");
            items.HasKey("OrderId", "Ordinal");

            items.Property(i => i.ProductId)
                .HasComment("Catalog product identifier.");
            items.Property(i => i.Quantity)
                .HasComment("Quantity of units (>= 1).");

            items.OwnsOne(i => i.ProductSnapshot, snapshot =>
            {
                snapshot.Property(s => s.Sku)
                    .HasColumnName("product_sku")
                    .HasMaxLength(ProductSnapshot.MaxSkuLength)
                    .HasComment("Product SKU snapshot (frozen at order creation).");
                snapshot.Property(s => s.Name)
                    .HasColumnName("product_name")
                    .HasMaxLength(ProductSnapshot.MaxNameLength)
                    .HasComment("Product display-name snapshot (frozen at order creation).");
            });
            items.Navigation(i => i.ProductSnapshot).IsRequired();

            items.OwnsOne(i => i.UnitPrice, price =>
            {
                price.Property(m => m.Amount)
                    .HasColumnName("unit_price_amount")
                    .HasPrecision(19, 4)
                    .HasComment("Per-unit price at checkout time.");
                price.Property(m => m.Currency)
                    .HasColumnName("unit_price_currency")
                    .HasMaxLength(3)
                    .HasComment("ISO 4217 currency code.")
                    .HasConversion(
                        c => c.Name,
                        name => CurrencyCode.FromName(name, ignoreCase: false));
            });
            items.Navigation(i => i.UnitPrice).IsRequired();

            items.OwnsOne(i => i.LineTotal, total =>
            {
                total.Property(m => m.Amount)
                    .HasColumnName("line_total_amount")
                    .HasPrecision(19, 4)
                    .HasComment("Quantity * UnitPrice (persisted to avoid recompute + map owned cleanly).");
                total.Property(m => m.Currency)
                    .HasColumnName("line_total_currency")
                    .HasMaxLength(3)
                    .HasComment("ISO 4217 currency code.")
                    .HasConversion(
                        c => c.Name,
                        name => CurrencyCode.FromName(name, ignoreCase: false));
            });
            items.Navigation(i => i.LineTotal).IsRequired();
        });
        builder.Metadata
            .FindNavigation(nameof(Order.Items))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }

    private static Action<OwnedNavigationBuilder<Order, Address>> ConfigureAddress(string prefix)
    {
        return address =>
        {
            address.Property(a => a.Street1)
                .HasColumnName($"{prefix}_street1_enc")
                .HasMaxLength(Address.Street1MaxLength)
                .HasComment("PII (ADR-0011): street line 1. v1 plaintext; v2 encrypts.");
            address.Property(a => a.Street2)
                .HasColumnName($"{prefix}_street2_enc")
                .HasMaxLength(Address.Street2MaxLength)
                .HasComment("PII (ADR-0011): street line 2 (optional).");
            address.Property(a => a.City)
                .HasColumnName($"{prefix}_city_enc")
                .HasMaxLength(Address.CityMaxLength)
                .HasComment("PII (ADR-0011): city.");
            address.Property(a => a.State)
                .HasColumnName($"{prefix}_state_enc")
                .HasMaxLength(Address.StateMaxLength)
                .HasComment("PII (ADR-0011): state/region (optional).");
            address.Property(a => a.PostalCode)
                .HasColumnName($"{prefix}_postal_code_enc")
                .HasMaxLength(Address.PostalCodeMaxLength)
                .HasComment("PII (ADR-0011): postal code.");
            address.Property(a => a.CountryCode)
                .HasColumnName($"{prefix}_country_code_enc")
                .HasMaxLength(Address.CountryCodeLength)
                .HasComment("ISO 3166-1 alpha-2 country code.");
        };
    }
}
