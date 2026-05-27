using System.Text;
using Invoicing.Domain.Common.ValueObjects;
using Invoicing.Domain.CreditNotes;
using Invoicing.Domain.CreditNotes.ValueObjects;
using Invoicing.Domain.Invoices;
using Invoicing.Domain.Invoices.ValueObjects;
using Invoicing.Infrastructure.Pdf;
using Microsoft.Extensions.Options;
using Platform.SharedKernel.ValueObjects;
using Xunit;

namespace Invoicing.IntegrationTests.Pdf;

/// <summary>
/// Integration tests for <see cref="QuestPdfInvoiceGenerator"/>. No Testcontainers needed
/// — PDF generation is pure in-process. The determinism tests are the primary acceptance
/// gate for ADR-0019 (two regenerations of the same aggregate must produce byte-identical
/// PDFs, so the SHA-256 content hash is stable across runs and across library upgrades).
/// </summary>
public sealed class QuestPdfInvoiceGeneratorTests
{
    private const int Sha256HexLength = 64;

    private static readonly DateTimeOffset FixedUtcNow = new(2026, 4, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] PdfMagic = Encoding.ASCII.GetBytes("%PDF-");

    [Fact]
    public async Task GenerateInvoiceAsync_ProducesValidPdf()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = CreateSut();
        var invoice = BuildIssuedInvoice(FixedUtcNow);

        var result = await sut.GenerateInvoiceAsync(invoice, ct);

        result.ContentType.Should().Be("application/pdf");
        result.SizeBytes.Should().BeGreaterThan(0);
        result.Content.Length.Should().Be((int)result.SizeBytes);
        result.Content.AsSpan(0, PdfMagic.Length).ToArray().Should().Equal(PdfMagic);
        AssertLowercaseHex(result.ContentHash);
    }

    [Fact]
    public async Task GenerateInvoiceAsync_IsDeterministic()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = CreateSut();

        var pdf1 = await sut.GenerateInvoiceAsync(BuildIssuedInvoice(FixedUtcNow), ct);
        var pdf2 = await sut.GenerateInvoiceAsync(BuildIssuedInvoice(FixedUtcNow), ct);

        pdf1.ContentHash.Should().Be(pdf2.ContentHash);
        pdf1.Content.Should().Equal(pdf2.Content);
    }

    [Fact]
    public async Task GenerateInvoiceAsync_DifferentIssueDate_ProducesDifferentHash()
    {
        // Guard against an accidental reintroduction of DateTime.UtcNow in the template —
        // if the template ignored IssueDate, both hashes would be equal.
        var ct = TestContext.Current.CancellationToken;
        var sut = CreateSut();

        var earlier = await sut.GenerateInvoiceAsync(BuildIssuedInvoice(FixedUtcNow), ct);
        var later = await sut.GenerateInvoiceAsync(
            BuildIssuedInvoice(FixedUtcNow.AddDays(1)), ct);

        later.ContentHash.Should().NotBe(earlier.ContentHash);
    }

    [Fact]
    public async Task GenerateCreditNoteAsync_ProducesValidPdf()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = CreateSut();
        var creditNote = BuildIssuedCreditNote(FixedUtcNow);

        var result = await sut.GenerateCreditNoteAsync(creditNote, ct);

        result.ContentType.Should().Be("application/pdf");
        result.SizeBytes.Should().BeGreaterThan(0);
        result.Content.AsSpan(0, PdfMagic.Length).ToArray().Should().Equal(PdfMagic);
        AssertLowercaseHex(result.ContentHash);
    }

    [Fact]
    public async Task GenerateCreditNoteAsync_IsDeterministic()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = CreateSut();

        var pdf1 = await sut.GenerateCreditNoteAsync(BuildIssuedCreditNote(FixedUtcNow), ct);
        var pdf2 = await sut.GenerateCreditNoteAsync(BuildIssuedCreditNote(FixedUtcNow), ct);

        pdf1.ContentHash.Should().Be(pdf2.ContentHash);
        pdf1.Content.Should().Equal(pdf2.Content);
    }

    private static QuestPdfInvoiceGenerator CreateSut() =>
        new(Options.Create(new PdfGenerationOptions
        {
            LegalEntityName = "Atlas Widgets Ltd.",
            LegalFooter = "VAT: CZ12345678 | Atlas Widgets Ltd., Prague, CZ",
        }));

    private static Invoice BuildIssuedInvoice(DateTimeOffset utcNow)
    {
        var eur = CurrencyCode.FromName("EUR");
        var line = InvoiceLine.Create(
            lineNumber: 1,
            sku: Sku.Create("WIDGET-001").Value,
            description: "Widget",
            quantity: 2,
            unitPrice: Money.Create(100m, eur).Value,
            vatRate: VatRate.Create(21m).Value).Value;

        var vatLine = VatLine.Create(
            VatRate.Create(21m).Value,
            Money.Create(200m, eur).Value,
            Money.Create(42m, eur).Value);

        var address = Platform.SharedKernel.ValueObjects.Address.Create(
            "Main Street 1", null, "Prague", null, "11000", "CZ").Value;

        var invoice = Invoice.Create(
            buyerId: new Guid("00000000-0000-0000-0000-000000000001"),
            orderId: new Guid("00000000-0000-0000-0000-000000000002"),
            paymentId: new Guid("00000000-0000-0000-0000-000000000003"),
            correlationId: new Guid("00000000-0000-0000-0000-000000000004"),
            billingAddress: address,
            lines: [line],
            vatLines: [vatLine],
            deliveryChannel: DeliveryChannel.Email,
            utcNow: utcNow).Value;

        var number = InvoiceNumber.Create(year: 2026, sequence: 142).Value;
        var pdfRef = PdfBlobRef.Create(
            "2026/04/INV-2026-000142.pdf",
            new string('a', 64),
            sizeBytes: 1024).Value;
        invoice.Issue(number, pdfRef, utcNow);
        return invoice;
    }

    private static CreditNote BuildIssuedCreditNote(DateTimeOffset utcNow)
    {
        var originalInvoice = BuildIssuedInvoice(utcNow);
        var creditNote = CreditNote.Create(
            originalInvoice.ToReversalSnapshot(utcNow),
            CreditNoteReason.OrderCancelled,
            correlationId: new Guid("00000000-0000-0000-0000-000000000005"),
            utcNow: utcNow).Value;

        var number = CreditNoteNumber.Create(year: 2026, sequence: 8).Value;
        var pdfRef = PdfBlobRef.Create(
            "2026/04/CN-2026-000008.pdf",
            new string('b', 64),
            sizeBytes: 1024).Value;
        creditNote.Issue(number, pdfRef, utcNow);
        return creditNote;
    }

    private static void AssertLowercaseHex(string hash)
    {
        hash.Should().HaveLength(Sha256HexLength);
        foreach (var ch in hash)
        {
            var isLowerHex = (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f');
            isLowerHex.Should().BeTrue($"ContentHash must be lowercase hex; saw '{ch}'");
        }
    }
}
