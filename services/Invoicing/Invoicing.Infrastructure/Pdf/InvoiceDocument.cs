using System.Globalization;
using Invoicing.Domain.Common.ValueObjects;
using Invoicing.Domain.Invoices;
using Platform.SharedKernel.ValueObjects;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Invoicing.Infrastructure.Pdf;

/// <summary>
/// QuestPDF <see cref="IDocument"/> rendering a fiscal invoice layout per ADR-0019 §
/// InvoiceDocument template. Deterministic by construction: every timestamp-ish field on
/// <see cref="DocumentMetadata"/> is derived from <see cref="Invoice.IssueDate"/> or a
/// constant, and numeric formatting goes through <see cref="CultureInfo.InvariantCulture"/>
/// so two generations on machines with different locales produce identical bytes.
/// </summary>
internal sealed class InvoiceDocument(Invoice invoice, PdfGenerationOptions options) : IDocument
{
    // Deferred — Inter font swap blocked on Dockerfile font-embedding work
    // (ADR-0019 § Font embedding; tracked as issue #134). The M10 marker is
    // removed because M10 has shipped; closeout1 L1.
    private const string DefaultFontFamily = "Lato";
    private const string MetadataCreator = "Atlas Invoicing";
    private const string MetadataProducer = "Atlas Invoicing";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public DocumentMetadata GetMetadata()
    {
        // QuestPDF defaults CreationDate/ModifiedDate to DateTime.Now and Producer to its
        // own versioned string. All three would break byte-determinism across runs and
        // across library upgrades (ADR-0019 § Risks). Every field below is either
        // aggregate-derived or a repo-owned constant.
        var issueInstant = invoice.IssueDate.UtcDateTime;
        var title = invoice.InvoiceNumber is null
            ? "Invoice (DRAFT)"
            : string.Create(Invariant, $"Invoice {invoice.InvoiceNumber.Value}");

        return new DocumentMetadata
        {
            Title = title,
            Author = options.LegalEntityName,
            Subject = "Invoice",
            Keywords = "invoice",
            Creator = MetadataCreator,
            Producer = MetadataProducer,
            CreationDate = issueInstant,
            ModifiedDate = issueInstant,
        };
    }

    public void Compose(IDocumentContainer container) =>
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(30);
            page.DefaultTextStyle(text => text.FontFamily(DefaultFontFamily).FontSize(10));

            page.Header().Element(ComposeHeader);
            page.Content().PaddingVertical(10).Column(column =>
            {
                column.Spacing(12);
                column.Item().Element(ComposeBuyerBlock);
                column.Item().Element(ComposeLineTable);
                column.Item().AlignRight().Element(ComposeVatBreakdown);
                column.Item().AlignRight().Element(ComposeTotals);
            });
            page.Footer().AlignCenter().Text(options.LegalFooter).FontSize(8);
        });

    private void ComposeHeader(IContainer container) =>
        container.Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text(options.LegalEntityName).Bold().FontSize(18);
                col.Item().Text("Invoice").FontSize(10);
            });
            row.ConstantItem(180).AlignRight().Column(col =>
            {
                col.Item().Text(invoice.InvoiceNumber?.Value ?? "DRAFT").Bold().FontSize(12);
                col.Item().Text(FormatDate(invoice.IssueDate)).FontSize(9);
            });
        });

    private void ComposeBuyerBlock(IContainer container) =>
        container.Column(column =>
        {
            column.Item().Text("Bill to:").SemiBold();
            var address = invoice.BillingAddress;
            column.Item().Text(address.Street1);
            if (!string.IsNullOrWhiteSpace(address.Street2))
            {
                column.Item().Text(address.Street2);
            }

            var cityLine = string.IsNullOrWhiteSpace(address.State)
                ? string.Create(Invariant, $"{address.PostalCode} {address.City}")
                : string.Create(Invariant, $"{address.PostalCode} {address.City}, {address.State}");
            column.Item().Text(cityLine);
            column.Item().Text(address.CountryCode);
        });

    private void ComposeLineTable(IContainer container) =>
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(30);   // #
                columns.RelativeColumn(2);    // SKU
                columns.RelativeColumn(5);    // Description
                columns.ConstantColumn(45);   // Qty
                columns.RelativeColumn(2);    // Unit Price
                columns.ConstantColumn(50);   // VAT %
                columns.RelativeColumn(2);    // Line Total
            });

            table.Header(header =>
            {
                header.Cell().Text("#").SemiBold();
                header.Cell().Text("SKU").SemiBold();
                header.Cell().Text("Description").SemiBold();
                header.Cell().AlignRight().Text("Qty").SemiBold();
                header.Cell().AlignRight().Text("Unit Price").SemiBold();
                header.Cell().AlignRight().Text("VAT %").SemiBold();
                header.Cell().AlignRight().Text("Line Total").SemiBold();
            });

            foreach (var line in invoice.Lines)
            {
                table.Cell().Text(line.LineNumber.ToString(Invariant));
                table.Cell().Text(line.Sku.Value);
                table.Cell().Text(line.Description);
                table.Cell().AlignRight().Text(line.Quantity.ToString(Invariant));
                table.Cell().AlignRight().Text(FormatMoney(line.UnitPrice));
                table.Cell().AlignRight().Text(FormatPercent(line.VatRate));
                table.Cell().AlignRight().Text(FormatMoney(line.LineTotal));
            }
        });

    private void ComposeVatBreakdown(IContainer container) =>
        container.Width(280).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
            });

            table.Header(header =>
            {
                header.Cell().Text("VAT").SemiBold();
                header.Cell().AlignRight().Text("Base").SemiBold();
                header.Cell().AlignRight().Text("Amount").SemiBold();
            });

            foreach (var vat in invoice.VatLines)
            {
                table.Cell().Text(FormatPercent(vat.Rate));
                table.Cell().AlignRight().Text(FormatMoney(vat.Base));
                table.Cell().AlignRight().Text(FormatMoney(vat.Amount));
            }
        });

    private void ComposeTotals(IContainer container) =>
        container.Width(280).Column(column =>
        {
            column.Spacing(2);
            column.Item().Row(row =>
            {
                row.RelativeItem().AlignLeft().Text("Subtotal").SemiBold();
                row.RelativeItem().AlignRight().Text(FormatMoney(invoice.Subtotal));
            });

            // Derive VAT total from the fiscal Total - Subtotal difference rather than
            // re-summing VatLines. Invariant I-1 on the aggregate guarantees equality,
            // and this keeps the footer row in lockstep with invoice.Total so the PDF
            // can never silently disagree with the aggregate's fiscal bottom line.
            var vatTotal = invoice.Total - invoice.Subtotal;

            column.Item().Row(row =>
            {
                row.RelativeItem().AlignLeft().Text("VAT total").SemiBold();
                row.RelativeItem().AlignRight().Text(FormatMoney(vatTotal));
            });

            column.Item().LineHorizontal(0.75f);

            column.Item().Row(row =>
            {
                row.RelativeItem().AlignLeft().Text("Total").Bold().FontSize(12);
                row.RelativeItem().AlignRight().Text(FormatMoney(invoice.Total)).Bold().FontSize(12);
            });
        });

    private static string FormatMoney(Money money) =>
        string.Create(Invariant, $"{money.Amount.ToString("F2", Invariant)} {money.Currency.Name}");

    private static string FormatPercent(VatRate rate) =>
        string.Create(Invariant, $"{rate.Percentage.ToString("0.##", Invariant)}%");

    private static string FormatDate(DateTimeOffset timestamp) =>
        timestamp.UtcDateTime.ToString("yyyy-MM-dd", Invariant);
}
