using Invoicing.Domain.Common.ValueObjects;
using Invoicing.Domain.Invoices;
using Invoicing.Domain.Invoices.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.SharedKernel.ValueObjects;

namespace Invoicing.Infrastructure.Persistence.Database.EntityConfigurations;

/// <summary>
/// EF Core mapping for the <see cref="Invoice"/> aggregate root.
/// <list type="bullet">
/// <item>Postgres <c>xmin</c> system column as the optimistic concurrency token via the
/// inherited <c>Entity.RowVersion</c> property (no stored column, Npgsql 10 maps
/// <c>uint + IsRowVersion()</c> to <c>xmin</c>).</item>
/// <item>PII <c>billing_address_*_enc</c> columns per ADR-0011 (v1 plaintext, v2 encrypts).</item>
/// <item><see cref="InvoiceNumber"/> persisted as a single VARCHAR(15) column via value converter
/// so the canonical <c>INV-YYYY-NNNNNN</c> string is the wire format. Nullable while
/// <see cref="InvoiceStatus.Draft"/>; populated atomically with the <c>Issued</c> transition.</item>
/// <item>Owned collections <c>invoice_lines</c> + <c>invoice_vat_lines</c> use field access
/// against <c>_lines</c> / <c>_vatLines</c> so the aggregate's encapsulation stays intact.</item>
/// </list>
/// </summary>
internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    private const int InvoiceNumberMaxLength = 15; // INV-YYYY-NNNNNN
    private const int CurrencyCodeLength = 3;
    private const int VatRatePrecision = 5;
    private const int VatRateScale = 2;
    private const int MoneyPrecision = 19;
    private const int MoneyScale = 4;
    private const int ContentHashLength = 64;

    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices", t => t.HasComment(
            "Invoice aggregate — fiscal record issued after order confirmation + payment capture."));

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id)
            .ValueGeneratedNever()
            .HasComment("Primary key (Guid v7 — time-ordered).");

        // Optimistic concurrency via Postgres xmin system column (matches Ordering precedent).
        builder.Property(i => i.RowVersion)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasComment("Optimistic concurrency token (Postgres xmin system column).");

        // InvoiceNumber: nullable until Issue() stamps it. Persist as VARCHAR(15) using the
        // canonical INV-YYYY-NNNNNN string; FromRaw validates shape on rehydrate.
        builder.Property(i => i.InvoiceNumber)
            .HasColumnName("invoice_number")
            .HasMaxLength(InvoiceNumberMaxLength)
            .HasConversion(
                n => n!.Value,
                s => InvoiceNumber.FromRaw(s).Value)
            .HasComment("Gap-free invoice number, format INV-YYYY-NNNNNN. Null while Draft.");

        // Unique partial index on InvoiceNumber once allocated (gap-free guarantee per ADR-0018
        // is at the allocator level; the unique index is defence-in-depth against double-issue
        // bugs). Filtered to non-null since drafts share NULL.
        builder.HasIndex(i => i.InvoiceNumber)
            .IsUnique()
            .HasFilter("invoice_number IS NOT NULL")
            .HasDatabaseName("ux_invoices_invoice_number");

        builder.Property(i => i.BuyerId)
            .HasComment("JWT sub of the buyer the invoice is issued to.");
        builder.HasIndex(i => i.BuyerId).HasDatabaseName("ix_invoices_buyer_id");

        builder.Property(i => i.OrderId)
            .HasComment("Reference to the Ordering Order the invoice settles.");
        // Unique — at most one Invoice per Order (M7 idempotency contract).
        builder.HasIndex(i => i.OrderId)
            .IsUnique()
            .HasDatabaseName("ux_invoices_order_id");

        builder.Property(i => i.PaymentId)
            .HasComment("Reference to the Payments transaction the invoice settles.");

        builder.Property(i => i.IssueDate)
            .HasComment("UTC timestamp when the invoice transitioned to Issued.");

        builder.Property(i => i.DeliveredAtUtc)
            .HasComment("UTC timestamp when the invoice transitioned to Delivered (nullable).");

        builder.Property(i => i.DeliveryNotificationId)
            .HasComment("NotificationId (ADR-0031) minted when delivery was requested; correlates the delivery confirmation. Null until Issued with a delivery channel.");

        // Correlation lookup for the delivery confirmation (Postgres unique index treats NULLs as
        // distinct, so the many Draft rows with NULL coexist with one row per issued NotificationId).
        builder.HasIndex(i => i.DeliveryNotificationId)
            .IsUnique()
            .HasDatabaseName("ux_invoices_delivery_notification_id");

        builder.Property(i => i.Status)
            .HasComment("Invoice lifecycle status (Draft|Issued|Delivered|Archived|Cancelled).")
            .HasConversion(
                status => status.Value,
                value => InvoiceStatus.FromValue(value));

        builder.Property(i => i.DeliveryChannel)
            .HasComment("Intended delivery channel (None|Email|TaxAuthorityWebhook).")
            .HasConversion(
                channel => channel.Value,
                value => DeliveryChannel.FromValue(value));

        // Billing address — owned PII per ADR-0011 (column suffix _enc reserves the contract for v2 DEK).
        builder.OwnsOne(i => i.BillingAddress, ConfigureAddress("billing_address"));
        builder.Navigation(i => i.BillingAddress).IsRequired();

        // Subtotal / Total as owned Money.
        builder.OwnsOne(i => i.Subtotal, money => ConfigureMoney(money, "subtotal"));
        builder.Navigation(i => i.Subtotal).IsRequired();

        builder.OwnsOne(i => i.Total, money => ConfigureMoney(money, "total"));
        builder.Navigation(i => i.Total).IsRequired();

        // PdfBlobRef — owned, nullable until Issue(). BlobName is the canonical immutable
        // identifier; the URI is computed on demand by callers via IBlobStore.GetSasUrlAsync
        // (issue #131 resolved). L4 (issue #137) still open: no DB CHECK constraint pins
        // Status to the SmartEnum value-set; defence-in-depth that needs a migration to land.
        builder.OwnsOne(i => i.PdfBlobRef, pdf =>
        {
            pdf.Property(p => p.BlobName)
                .HasColumnName("pdf_blob_name")
                .HasMaxLength(PdfBlobRef.BlobNameMaxLength)
                .IsRequired();

            pdf.Property(p => p.ContentHash)
                .HasColumnName("pdf_content_hash")
                .HasMaxLength(ContentHashLength)
                .IsFixedLength()
                .HasComment("SHA-256 of the PDF bytes, lowercase hex (64 chars).");
            pdf.Property(p => p.SizeBytes)
                .HasColumnName("pdf_size_bytes")
                .HasComment("PDF size in bytes (>0).");
        });

        // CancellationInfo — owned, nullable; populated only on Cancel() per I-6.
        builder.OwnsOne(i => i.CancellationInfo, info =>
        {
            info.Property(c => c.CancelledAtUtc)
                .HasColumnName("cancelled_at_utc")
                .HasComment("UTC timestamp when the invoice transitioned to Cancelled.");
            info.Property(c => c.Reason)
                .HasColumnName("cancellation_reason")
                .HasComment("CreditNoteReason explaining why the invoice was cancelled.")
                .HasConversion(
                    reason => reason.Value,
                    value => CreditNoteReason.FromValue(value));
            info.Property(c => c.CreditNoteId)
                .HasColumnName("cancellation_credit_note_id")
                .HasComment("Identifier of the reversing CreditNote (Invoice invariant I-6).");
        });

        // Lines owned collection — backing field _lines.
        builder.OwnsMany(i => i.Lines, lines =>
        {
            lines.ToTable("invoice_lines", t => t.HasComment(
                "Invoice line items — frozen at issuance per Invoice invariant I-2."));
            lines.WithOwner().HasForeignKey("InvoiceId");
            lines.HasKey("InvoiceId", nameof(InvoiceLine.LineNumber));

            ConfigureInvoiceLine(lines);
        });
        builder.Metadata
            .FindNavigation(nameof(Invoice.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // VatLines owned collection — backing field _vatLines.
        builder.OwnsMany(i => i.VatLines, vatLines =>
        {
            vatLines.ToTable("invoice_vat_lines", t => t.HasComment(
                "Per-rate VAT breakdown for the invoice. Empty when every line is at 0%."));
            vatLines.WithOwner().HasForeignKey("InvoiceId");
            vatLines.Property<int>("Ordinal");
            vatLines.HasKey("InvoiceId", "Ordinal");

            vatLines.Property(v => v.Rate)
                .HasColumnName("rate_percentage")
                .HasPrecision(VatRatePrecision, VatRateScale)
                .HasConversion(
                    rate => rate.Percentage,
                    value => VatRate.Create(value).Value)
                .HasComment("VAT rate percentage in [0, 100], 2 decimals.");

            vatLines.OwnsOne(v => v.Base, money => ConfigureMoney(money, "base"));
            vatLines.Navigation(v => v.Base).IsRequired();

            vatLines.OwnsOne(v => v.Amount, money => ConfigureMoney(money, "amount"));
            vatLines.Navigation(v => v.Amount).IsRequired();
        });
        builder.Metadata
            .FindNavigation(nameof(Invoice.VatLines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }

    internal static void ConfigureInvoiceLine<TOwner>(OwnedNavigationBuilder<TOwner, InvoiceLine> lines)
        where TOwner : class
    {
        lines.Property(l => l.LineNumber)
            .HasComment("Position on the document (1-based).");
        lines.Property(l => l.Description)
            .HasMaxLength(InvoiceLine.MaxDescriptionLength)
            .HasComment("Human-readable line description.");
        lines.Property(l => l.Quantity)
            .HasComment("Units on the line (>= 1).");

        lines.Property(l => l.Sku)
            .HasColumnName("sku")
            .HasMaxLength(Sku.MaxLength)
            .HasConversion(
                sku => sku.Value,
                value => Sku.Create(value).Value)
            .HasComment("Catalog SKU snapshot at issuance.");

        lines.Property(l => l.VatRate)
            .HasColumnName("vat_rate_percentage")
            .HasPrecision(VatRatePrecision, VatRateScale)
            .HasConversion(
                rate => rate.Percentage,
                value => VatRate.Create(value).Value)
            .HasComment("Applicable VAT rate, in [0, 100].");

        lines.OwnsOne(l => l.UnitPrice, money => ConfigureMoney(money, "unit_price"));
        lines.Navigation(l => l.UnitPrice).IsRequired();

        lines.OwnsOne(l => l.LineTotal, money => ConfigureMoney(money, "line_total"));
        lines.Navigation(l => l.LineTotal).IsRequired();
    }

    /// <summary>
    /// Parallel mapping for <see cref="CreditNoteLine"/>. Structurally identical column shape
    /// to <see cref="ConfigureInvoiceLine{TOwner}"/> — the table layout is unchanged — but the
    /// owned-type discriminator differs because <see cref="CreditNoteLine"/> is a distinct
    /// domain concept (corrections/returns/goodwill, backward-looking lifecycle) rather than
    /// a sign-flipped invoice line.
    /// </summary>
    internal static void ConfigureCreditNoteLine<TOwner>(OwnedNavigationBuilder<TOwner, CreditNoteLine> lines)
        where TOwner : class
    {
        lines.Property(l => l.LineNumber)
            .HasComment("Position on the credit note (1-based; mirrors the original invoice line's number).");
        lines.Property(l => l.Description)
            .HasMaxLength(CreditNoteLine.MaxDescriptionLength)
            .HasComment("Human-readable line description (copied from the source invoice line).");
        lines.Property(l => l.Quantity)
            .HasComment("Units being credited (>= 1).");

        lines.Property(l => l.Sku)
            .HasColumnName("sku")
            .HasMaxLength(Sku.MaxLength)
            .HasConversion(
                sku => sku.Value,
                value => Sku.Create(value).Value)
            .HasComment("Catalog SKU snapshot from the reversed invoice line.");

        lines.Property(l => l.VatRate)
            .HasColumnName("vat_rate_percentage")
            .HasPrecision(VatRatePrecision, VatRateScale)
            .HasConversion(
                rate => rate.Percentage,
                value => VatRate.Create(value).Value)
            .HasComment("VAT rate from the reversed invoice line, in [0, 100].");

        lines.OwnsOne(l => l.UnitPrice, money => ConfigureMoney(money, "unit_price"));
        lines.Navigation(l => l.UnitPrice).IsRequired();

        lines.OwnsOne(l => l.LineTotal, money => ConfigureMoney(money, "line_total"));
        lines.Navigation(l => l.LineTotal).IsRequired();
    }

    internal static void ConfigureMoney<TOwner>(OwnedNavigationBuilder<TOwner, Money> money, string prefix)
        where TOwner : class
    {
        money.Property(m => m.Amount)
            .HasColumnName($"{prefix}_amount")
            .HasPrecision(MoneyPrecision, MoneyScale);
        money.Property(m => m.Currency)
            .HasColumnName($"{prefix}_currency")
            .HasMaxLength(CurrencyCodeLength)
            .HasConversion(
                c => c.Name,
                name => CurrencyCode.FromName(name, ignoreCase: false));
    }

    private static Action<OwnedNavigationBuilder<Invoice, Address>> ConfigureAddress(string prefix)
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
