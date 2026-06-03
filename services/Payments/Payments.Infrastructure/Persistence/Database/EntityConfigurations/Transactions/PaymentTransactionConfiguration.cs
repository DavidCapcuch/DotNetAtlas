using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Payments.Domain.Transactions;
using Payments.Domain.Transactions.ValueObjects;
using Platform.SharedKernel.ValueObjects;

namespace Payments.Infrastructure.Persistence.Database.EntityConfigurations.Transactions;

/// <summary>
/// EF Core mapping for the <see cref="PaymentTransaction"/> aggregate root. Applies:
/// <list type="bullet">
/// <item>Postgres <c>xmin</c> system column as optimistic concurrency token via the inherited
/// <see cref="Platform.SharedKernel.Base.Entity{TId}.RowVersion"/> property +
/// <see cref="RelationalPropertyBuilderExtensions.HasColumnName"/> — matches the
/// codebase-wide Ordering / Weather convention.</item>
/// <item>PII <c>*_enc</c> column suffixes on <see cref="PaymentTransaction.PaymentMethodId"/>
/// and <see cref="PaymentTransaction.GatewayTransactionId"/> per ADR-0011 (v1 plaintext;
/// v2 encrypts per-buyer DEK).</item>
/// <item>Owned <see cref="Money"/>, <see cref="ValueObjects.GatewayResponseCode"/>, and
/// <see cref="ValueObjects.FailureInfo"/> value objects flattened onto sibling columns.</item>
/// <item>SmartEnum conversions for <see cref="PaymentStatus"/> + <see cref="FailureReason"/>.</item>
/// <item>Indexes: unique <c>ux_payment_transactions_order_id</c> (one-payment-per-order,
/// ADR-0029) + non-unique <c>ix_payment_transactions_buyer_id</c> for admin lookups.</item>
/// </list>
/// </summary>
internal sealed class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
{
    public void Configure(EntityTypeBuilder<PaymentTransaction> builder)
    {
        builder.ToTable("payment_transactions", t => t.HasComment(
            "PaymentTransaction aggregate — saga-scoped lifecycle from Requested through Completed/Failed/Voided/Refunded."));

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .ValueGeneratedNever()
            .HasComment("Primary key — saga-minted UUID v7 (time-ordered), carried on AuthorizePaymentCommand as PaymentTransactionId; distinct from the saga key (OrderId). One payment per order is enforced by the ux_payment_transactions_order_id unique index (ADR-0029). See docs/bc-design/payments.md § 2.2 (I-7).");

        // Optimistic concurrency via Postgres xmin system column. `Entity.RowVersion` is
        // inherited from Platform.SharedKernel; Npgsql's RowVersion convention maps it to
        // xmin (no stored column).
        builder.Property(t => t.RowVersion)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasComment("Optimistic concurrency token (Postgres xmin system column).");

        builder.Property(t => t.CorrelationId)
            .HasComment("Originating saga correlation id (== OrderId per ADR-0029; links checkout / order / invoice).");

        builder.Property(t => t.BuyerId)
            .HasComment("JWT sub of the buyer the payment is for.");
        builder.HasIndex(t => t.BuyerId)
            .HasDatabaseName("ix_payment_transactions_buyer_id");

        builder.Property(t => t.OrderId)
            .HasComment("Ordering aggregate id this payment is attached to (frozen at creation). Unique index enforces one payment per order (ADR-0029).");
        builder.HasIndex(t => t.OrderId)
            .IsUnique()
            .HasDatabaseName("ux_payment_transactions_order_id");

        builder.Property(t => t.Status)
            .HasComment("Lifecycle status (Requested / Authorized / Captured / Completed / Failed / Voided / Refunded).")
            .HasConversion(
                status => status.Value,
                value => PaymentStatus.FromValue(value));

        // PII per ADR-0011 — gateway-issued tokenised payment instrument (never PAN/CVV).
        // *_enc suffix reserves the contract for v2 per-buyer DEK encryption.
        builder.Property(t => t.PaymentMethodId)
            .HasColumnName("payment_method_id_enc")
            .HasMaxLength(64)
            .HasConversion(
                vo => vo.Value,
                value => PaymentMethodId.Create(value).Value)
            .HasComment("PII (ADR-0011): gateway-issued tokenised payment instrument. v1 plaintext; v2 encrypts.");

        // Sensitive token per ADR-0011 — gateway-issued transaction reference, append-only.
        builder.Property(t => t.GatewayTransactionId)
            .HasColumnName("gateway_transaction_id_enc")
            .HasMaxLength(256)
            .HasComment("Sensitive token (ADR-0011): gateway transaction reference; null until first successful gateway call. v1 plaintext; v2 encrypts.");

        // Business-time timestamps — DateTimeOffset persisted as timestamptz by Npgsql convention (ADR-0015).
        builder.Property(t => t.AuthorizedAtUtc).HasComment("UTC timestamp when authorize succeeded (nullable).");
        builder.Property(t => t.CapturedAtUtc).HasComment("UTC timestamp when capture succeeded (nullable).");
        builder.Property(t => t.CompletedAtUtc).HasComment("UTC timestamp when capture auto-advanced to Completed (nullable).");
        builder.Property(t => t.RefundedAtUtc).HasComment("UTC timestamp when refund completed (nullable).");
        builder.Property(t => t.VoidedAtUtc).HasComment("UTC timestamp when authorization was voided (nullable).");

        // H-5: saga-supplied void reason — captured in plain text for audit; null until Void succeeds.
        builder.Property(t => t.VoidReason)
            .HasColumnName("void_reason")
            .HasMaxLength(256)
            .HasComment("Saga-supplied reason for the void (H-5 closeout; nullable until Void succeeds).");

        // Owned Money — flat amount + currency.
        builder.OwnsOne(t => t.Amount, amount =>
        {
            amount.Property(m => m.Amount)
                .HasColumnName("amount")
                .HasPrecision(19, 4)
                .HasComment("Payment amount.");
            amount.Property(m => m.Currency)
                .HasColumnName("amount_currency")
                .HasMaxLength(3)
                .HasComment("ISO 4217 currency code.")
                .HasConversion(
                    c => c.Name,
                    name => CurrencyCode.FromName(name, ignoreCase: false));
        });
        builder.Navigation(t => t.Amount).IsRequired();

        // Owned GatewayResponseCode — last observed gateway response (raw forensic data).
        builder.OwnsOne(t => t.GatewayResponseCode, code =>
        {
            code.Property(c => c.Code)
                .HasColumnName("gateway_response_code")
                .HasMaxLength(64)
                .HasComment("Last observed gateway response code (forensic; not used for control flow).");
            code.Property(c => c.Message)
                .HasColumnName("gateway_response_message")
                .HasMaxLength(512)
                .HasComment("Last observed gateway response human-readable message.");
        });

        // Owned FailureInfo — populated only when Status reaches Failed.
        builder.OwnsOne(t => t.FailureInfo, failure =>
        {
            failure.Property(f => f.Reason)
                .HasColumnName("failure_reason")
                .HasComment("Classified terminal failure reason.")
                .HasConversion(
                    reason => reason.Value,
                    value => FailureReason.FromValue(value));
            failure.Property(f => f.GatewayCode)
                .HasColumnName("failure_gateway_code")
                .HasMaxLength(64)
                .HasComment("Raw gateway code at failure time (nullable when gateway returned no code).");
            failure.Property(f => f.RecordedAtUtc)
                .HasColumnName("failure_recorded_at_utc")
                .HasComment("UTC timestamp when the failure was observed and recorded on the aggregate.");
        });
    }
}
