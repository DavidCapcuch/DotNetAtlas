using System.Globalization;
using Invoicing.Domain.Common.ValueObjects;
using Invoicing.Domain.CreditNotes;
using Platform.SharedKernel.ValueObjects;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Invoicing.Infrastructure.Pdf;

/// <summary>
/// QuestPDF <see cref="IDocument"/> rendering a credit-note layout that references its
/// originating invoice and shows the sign-flipped reversing lines. Shares the same
/// determinism contract as <see cref="InvoiceDocument"/> — no <c>DateTime.UtcNow</c>,
/// locale-invariant number formatting, constant <c>Creator</c>/<c>Producer</c> metadata.
/// </summary>
internal sealed class CreditNoteDocument(CreditNote creditNote, PdfGenerationOptions options) : IDocument
{
    private const string DefaultFontFamily = "Lato"; // TODO: Swap to "Inter" once Dockerfile embeds fonts per ADR-0019 § Font embedding.
    private const string MetadataCreator = "Atlas Invoicing";
    private const string MetadataProducer = "Atlas Invoicing";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public DocumentMetadata GetMetadata()
    {
        var issueInstant = creditNote.IssueDate.UtcDateTime;
        var title = creditNote.CreditNoteNumber is null
            ? "Credit Note (DRAFT)"
            : string.Create(Invariant, $"Credit Note {creditNote.CreditNoteNumber.Value}");

        return new DocumentMetadata
        {
            Title = title,
            Author = options.LegalEntityName,
            Subject = "Credit Note",
            Keywords = "credit-note",
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
                column.Item().Element(ComposeReferenceBlock);
                column.Item().Element(ComposeLineTable);
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
                col.Item().Text("Credit Note").FontSize(10);
            });
            row.ConstantItem(180).AlignRight().Column(col =>
            {
                col.Item().Text(creditNote.CreditNoteNumber?.Value ?? "DRAFT").Bold().FontSize(12);
                col.Item().Text(FormatDate(creditNote.IssueDate)).FontSize(9);
            });
        });

    private void ComposeReferenceBlock(IContainer container) =>
        container.Column(column =>
        {
            column.Item().Text("Reverses invoice:").SemiBold();
            column.Item().Text(creditNote.OriginalInvoiceNumber.Value);
            column.Item().PaddingTop(4).Text("Reason:").SemiBold();
            column.Item().Text(creditNote.Reason.Name);
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

            foreach (var line in creditNote.Lines)
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

    private void ComposeTotals(IContainer container) =>
        container.Width(280).Column(column =>
        {
            column.Spacing(2);
            column.Item().LineHorizontal(0.75f);
            column.Item().Row(row =>
            {
                row.RelativeItem().AlignLeft().Text("Total (credit)").Bold().FontSize(12);
                row.RelativeItem().AlignRight().Text(FormatMoney(creditNote.Total)).Bold().FontSize(12);
            });
        });

    private static string FormatMoney(Money money) =>
        string.Create(Invariant, $"{money.Amount.ToString("F2", Invariant)} {money.Currency.Name}");

    private static string FormatPercent(VatRate rate) =>
        string.Create(Invariant, $"{rate.Percentage.ToString("0.##", Invariant)}%");

    private static string FormatDate(DateTimeOffset timestamp) =>
        timestamp.UtcDateTime.ToString("yyyy-MM-dd", Invariant);
}
