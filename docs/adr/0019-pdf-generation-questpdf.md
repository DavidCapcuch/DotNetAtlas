# ADR-0019: PDF Generation Library — QuestPDF

## Status

Accepted (2026-04-19)

## Context

The Invoicing BC generates PDF invoices and credit notes. The document must:

- Be generated programmatically from aggregate state (line items, VAT, buyer address, legal footer).
- Be deterministic — same input produces byte-identical output (for hash-based integrity checks per [invoicing.md § 15](../bc-design/invoicing.md) testing strategy).
- Support Unicode (international buyer names, addresses).
- Embed fonts consistently across environments (no "rendered OK on dev, wrong font in prod").
- Run in a Linux container (Alpine or Debian-based) without GDI+, GhostScript, or other external binaries.
- Have permissive licensing compatible with both the reference solution's learning use and potential production use.

The .NET PDF-library ecosystem has three strong contenders:

- **[QuestPDF](https://www.questpdf.com/)** — MIT (community edition, see § License); fluent C# DSL; modern developer experience; actively maintained.
- **[PDFsharp](http://www.pdfsharp.net/)** — MIT; lower-level page drawing API; mature (since 2005); smaller footprint.
- **[iTextSharp / iText 7 .NET](https://itextpdf.com/)** — AGPL (free for open-source) or commercial license; most feature-complete; enterprise-grade.

## Decision Drivers (ranked)

1. **Declarative composition for a template** — invoice layout is best expressed declaratively (header / buyer block / line table / VAT summary / footer).
2. **Deterministic output** — same input must produce byte-identical PDF for integrity-test purposes.
3. **Linux-container friendly** — no GDI+, no native dependencies beyond the NuGet package.
4. **License compatibility** — AGPL is a non-starter for a reference solution (viral licensing); MIT or MIT-equivalent required.
5. **Readable template code** — an adopter reading `InvoiceDocument.cs` should be able to understand the PDF structure without learning a new API paradigm.

## Considered Options

### Option 1: QuestPDF

Fluent C# DSL: `container.Column(col => { col.Item().Text("Invoice"); col.Item().Table(table => { ... }); })`. MIT-licensed community edition (QuestPDF Professional license required above $1M annual revenue — see § License).

### Option 2: PDFsharp

Imperative API: `var gfx = XGraphics.FromPdfPage(page); gfx.DrawString("Invoice", font, brush, 50, 50);`. MIT-licensed.

### Option 3: iText 7 .NET

Full-featured; imperative + declarative blend. AGPL or paid commercial license.

### Option 4: Pre-rendered template + ASP.NET Razor → WeasyPrint / Playwright HTML-to-PDF

Render HTML template via Razor; convert to PDF via Playwright (Chromium) or WeasyPrint. Externalizes layout to HTML+CSS.

## Evaluation Matrix

| Driver (ranked) | Option 1: QuestPDF | Option 2: PDFsharp | Option 3: iText 7 | Option 4: HTML→PDF |
|---|---|---|---|---|
| 1. Declarative composition | Excellent — purpose-built fluent DSL | Imperative drawing | Partially declarative (ElementGroup etc.) | HTML+CSS is declarative |
| 2. Deterministic output | Yes (per docs + test verification) | Yes | Yes | No — Chromium renderer output varies by version |
| 3. Linux-container friendly | Yes (pure managed) | Yes (pure managed) | Yes | No — Playwright requires Chromium binary; WeasyPrint requires GTK |
| 4. License | MIT (with revenue threshold — see § License) | MIT | AGPL or commercial | Playwright: Apache 2.0; WeasyPrint: BSD |
| 5. Readable template | Clear fluent chain; reads like HTML | Procedural — harder to skim | Verbose | Native HTML — readable but adds a templating layer |

## Decision

We will use **Option 1: QuestPDF** under its MIT community license for the reference solution. Invoicing's `InvoiceDocument` is the canonical PDF template implementation.

## Rationale

The reference solution optimizes for readable, teachable template code. QuestPDF's fluent DSL — `container.Column()` / `.Row()` / `.Table()` — reads like a structured layout declaration rather than a set of coordinate-based drawing calls. A reader opening `InvoiceDocument.cs` sees the document's structure at a glance (header, buyer block, line-item table, VAT summary, total, footer) without needing to mentally reconstruct a layout from absolute positions. That's a direct pedagogical win over Options 2 and 3.

Determinism is table-stakes for the integrity-hash test. QuestPDF documents that deterministic output is a design goal; in practice the integration test we've specified (hash two generations and assert equality) will catch any regressions.

License is the hard constraint. iText 7 (Option 3) is AGPL-licensed: any product using iText must also be AGPL, which is viral and hostile to downstream adoption of the reference solution by anyone planning closed-source production use. QuestPDF's community license is MIT with a revenue threshold ($1M annual revenue) above which a commercial license is required. At reference-solution scope, this is irrelevant; any adopter exceeding the threshold can purchase a license or swap the library (the `IPdfGenerator` abstraction in § Implementation Notes makes this a one-file change).

Option 4 (HTML→PDF) has the best "just-use-what-you-know-as-a-web-dev" ergonomics but fails on the Linux-container-friendly requirement: Playwright needs Chromium, which balloons image size from ~200MB to ~1.5GB. WeasyPrint needs GTK and Pango — same problem. For a reference solution that runs on a developer laptop via docker-compose, the bloat isn't worth the ergonomic win.

## Consequences

### Positive

- Declarative template — readers understand the PDF structure from 30 lines of fluent C#.
- Deterministic output supports the byte-hash integrity test (example-mapping § 2.1 for credit notes, § 4.1 for invoices).
- Pure-managed library — no native binaries, no GDI+, clean Alpine-Linux containers.
- MIT license (community edition) at reference-solution scale.
- Active maintainership and rich docs (see § Migration / swap if license terms change).

### Negative

- Revenue-threshold commercial license for large adopters. Mitigation: `IPdfGenerator` abstraction makes swap trivial (see § Implementation Notes).
- Fluent DSL is non-transferable to non-.NET stacks. If the solution is later ported to another language, templates must be rewritten.
- QuestPDF adds ~10 MB to the Invoicing container image. Acceptable.

### Risks

- **License evolution** — QuestPDF's terms could change in the future. Mitigation: `IPdfGenerator` abstraction; PDFsharp is a drop-in Option 2 fallback at slightly more verbose template code.
- **Determinism regression in a QuestPDF update** — a minor version bump could change pixel-level rendering. Mitigation: version-pin QuestPDF in `Directory.Packages.props`; the integrity-hash test catches regressions; bump deliberately.
- **Font availability in Docker image** — QuestPDF defaults to system fonts; if the container image lacks the requested font, PDF rendering may fall back unpredictably. Mitigation: embed a small set of fonts (one sans-serif, one monospace) in the Invoicing container image and reference them explicitly in the template.
- **Locale / RTL rendering** — QuestPDF supports Unicode, RTL is well-supported per docs. Tested via a buyer whose name is in Arabic (integration test).

## Implementation Notes

### License posture

- Community (MIT) edition used.
- Reference-solution repository LICENSE.md notes: "Invoicing uses QuestPDF under the Community MIT License. Adopters exceeding QuestPDF's revenue threshold must obtain a QuestPDF Professional license or swap to an alternative via the `IPdfGenerator` abstraction."

### `IPdfGenerator` abstraction

Keeps the library choice swappable. In `Invoicing.Application`:

```csharp
public interface IPdfGenerator
{
    Task<PdfGenerationResult> GenerateInvoiceAsync(Invoice invoice, CancellationToken ct);
    Task<PdfGenerationResult> GenerateCreditNoteAsync(CreditNote creditNote, CancellationToken ct);
}

public readonly record struct PdfGenerationResult(byte[] Content, string ContentHash, long SizeBytes, string ContentType);
```

Adapter in `Invoicing.Infrastructure.Pdf.QuestPdf`:

```csharp
public sealed class QuestPdfInvoiceGenerator : IPdfGenerator { ... }
```

### `InvoiceDocument` template (QuestPDF `IDocument`)

Skeleton:

```csharp
public sealed class InvoiceDocument(Invoice invoice, InvoicingOptions options) : IDocument
{
    public DocumentMetadata GetMetadata() => new() {
        Title = $"Invoice {invoice.InvoiceNumber.Value}",
        Author = options.LegalEntityName,
        CreationDate = invoice.IssueDate.UtcDateTime,  // deterministic: from aggregate, not DateTime.UtcNow
    };

    public void Compose(IDocumentContainer container) =>
        container.Page(page => {
            page.Size(PageSizes.A4);
            page.Margin(30);

            page.Header().Row(row => {
                row.RelativeItem().Text(options.LegalEntityName).Bold().FontSize(18);
                row.ConstantItem(120).AlignRight().Text($"INV {invoice.InvoiceNumber.Value}").FontSize(10);
            });

            page.Content().PaddingVertical(10).Column(col => {
                col.Spacing(10);
                col.Item().Element(ComposeBuyerBlock);
                col.Item().Element(ComposeLineTable);
                col.Item().AlignRight().Element(ComposeTotals);
            });

            page.Footer().AlignCenter().Text(options.LegalFooter).FontSize(8);
        });

    private void ComposeBuyerBlock(IContainer container) { /* ... */ }
    private void ComposeLineTable(IContainer container) { /* ... */ }
    private void ComposeTotals(IContainer container) { /* ... */ }
}
```

**Determinism requirement:** `GetMetadata().CreationDate` uses `invoice.IssueDate` (aggregate state), NOT `DateTime.UtcNow`. This ensures two regenerations produce identical PDFs (same content hash).

### Font embedding

```csharp
QuestPDF.Settings.FontDiscoveryPaths.Add("/app/fonts");  // Dockerfile COPYs fonts/ to this path
Fonts.RegisterFontsFromFolder("/app/fonts");
```

Invoicing's Dockerfile:

```dockerfile
COPY ./Invoicing/Invoicing.Api/fonts /app/fonts
```

Specific fonts: Inter (sans-serif), JetBrains Mono (monospace) — both SIL Open Font License.

### Determinism test

Integration test `InvoiceDocumentIsDeterministic`:

```csharp
var pdf1 = await sut.GenerateInvoiceAsync(sampleInvoice, CancellationToken.None);
var pdf2 = await sut.GenerateInvoiceAsync(sampleInvoice, CancellationToken.None);
pdf1.ContentHash.Should().Be(pdf2.ContentHash);
pdf1.Content.Should().BeEquivalentTo(pdf2.Content);
```

This test also serves as a smoke for unexpected library-version side effects after a NuGet update.

### Performance

QuestPDF performance for a 10-line invoice:

- Generation: ~50ms on a typical dev machine
- Binary size: ~30 KB PDF for a typical invoice

At reference-solution rate (≤ 5 issuances/sec), total CPU spent on PDF generation is < 1% of a core.

### Observability

- `invoicing.pdf.generation.duration.seconds` (histogram)
- `invoicing.pdf.size.bytes` (histogram)
- Span attribute `invoicing.pdf.page_count` for multi-page invoices (v2 — most v1 invoices are single-page)

### Swap to PDFsharp (if needed)

If QuestPDF's license terms become unacceptable, swap:

1. Implement `PdfSharpInvoiceGenerator : IPdfGenerator` in `Invoicing.Infrastructure.Pdf.PdfSharp`.
2. DI rewires `services.AddScoped<IPdfGenerator, PdfSharpInvoiceGenerator>()`.
3. Determinism test continues to validate byte-identical output.
4. Template code is more verbose (coordinate-based) but functionally equivalent.

Adapter swap effort: ~1 day of work, zero change to application code.

## Related Decisions

- [ADR-0017: Blob Storage + CDN](0017-blob-storage-cdn.md) — stores the PDFs this ADR generates
- [ADR-0018: Invoice Numbering](0018-invoice-numbering.md) — allocates the number the PDF displays
- [ADR-0015: Time & Timezone Policy](0015-time-timezone-policy.md) — `invoice.IssueDate` drives deterministic PDF metadata
- [ADR-0009: Reference-Solution Target Profile](0009-reference-solution-target-profile.md) — PDF generation rate fits the profile
