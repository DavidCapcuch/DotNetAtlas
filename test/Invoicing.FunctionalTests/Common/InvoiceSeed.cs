using Invoicing.Domain.Common.ValueObjects;
using Invoicing.Domain.CreditNotes;
using Invoicing.Domain.CreditNotes.ValueObjects;
using Invoicing.Domain.Invoices;
using Invoicing.Domain.Invoices.ValueObjects;
using Invoicing.Infrastructure.Persistence.Database;
using Microsoft.Extensions.Time.Testing;
using Platform.SharedKernel.ValueObjects;

namespace Invoicing.FunctionalTests.Common;

/// <summary>
/// Hand-rolled fluent seed for the <see cref="Invoice"/> + <see cref="CreditNote"/>
/// aggregates. Each <c>Build*</c> overload walks the FSM via the aggregate's own
/// factory and transition methods so the seed produces real domain events / row-version
/// bumps — i.e., it is byte-identical to a production-emitted invoice.
/// </summary>
internal sealed class InvoiceSeed
{
    private const string SampleContentHash =
        "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    /// <summary>
    /// Builds a fresh <see cref="Address"/> per invoice. EF maps <c>BillingAddress</c> as
    /// an OwnedOne whose identifying key is <c>InvoiceId</c>; sharing one VO instance
    /// across two invoices fails the change tracker (cf. PdfBlobRef rationale on
    /// <see cref="BuildPdfBlobRef"/>).
    /// </summary>
    private static Address BuildBillingAddress() => Address.Create(
        street1: "Wenceslas Square 1",
        street2: null,
        city: "Prague",
        state: null,
        postalCode: "11000",
        countryCode: "CZ").Value;

    private static PdfBlobRef BuildPdfBlobRef(string documentNumber) =>
        // Each invoice / credit note must own a distinct PdfBlobRef instance because EF
        // treats the owned-entity FK (InvoiceId / CreditNoteId) as the identifying key —
        // attaching the same VO instance to two parents fails with
        // "property is part of a key and so cannot be modified".
        PdfBlobRef.Create(
            new Uri($"https://test.blob.local/invoices/2026/01/{documentNumber}.pdf?sv=stub"),
            contentHash: SampleContentHash,
            sizeBytes: 1024).Value;

    private readonly InvoicingDbContext _db;
    private readonly FakeTimeProvider _time;
    private long _nextInvoiceSeq = 1;
    private long _nextCreditNoteSeq = 1;

    public InvoiceSeed(InvoicingDbContext db, FakeTimeProvider time)
    {
        _db = db;
        _time = time;
    }

    /// <summary>
    /// Persists an <c>Issued</c>-status invoice with one line, billing address, and a
    /// stamped PdfBlobRef. Returns the persisted aggregate (post-SaveChanges).
    /// </summary>
    public async Task<Invoice> CreateIssuedInvoiceAsync(Guid buyerId, Guid? orderId = null)
    {
        var invoice = BuildIssuedInvoice(buyerId, orderId ?? Guid.CreateVersion7());
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();
        return invoice;
    }

    /// <summary>
    /// Persists a <c>Draft</c> invoice (no number, no PDF) — used to assert the resend
    /// endpoint returns 409 for invoices that have not yet been issued.
    /// </summary>
    public async Task<Invoice> CreateDraftInvoiceAsync(Guid buyerId)
    {
        var invoice = BuildDraftInvoice(buyerId);
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();
        return invoice;
    }

    /// <summary>
    /// Persists an issued invoice plus a reversing credit note; returns the credit note.
    /// The original invoice transitions to <c>Cancelled</c>.
    /// </summary>
    public async Task<CreditNote> CreateIssuedCreditNoteAsync(Guid buyerId)
    {
        var invoice = BuildIssuedInvoice(buyerId, Guid.CreateVersion7());
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();

        var utcNow = _time.GetUtcNow();
        var creditNote = CreditNote.Create(
            originalInvoice: invoice,
            reason: CreditNoteReason.OrderCancelled,
            correlationId: invoice.CorrelationId,
            utcNow: utcNow).Value;

        var creditNoteNumber = CreditNoteNumber.Create(utcNow.Year, _nextCreditNoteSeq++).Value;
        creditNote.AssignCreditNoteNumber(creditNoteNumber);
        creditNote.Issue(BuildPdfBlobRef(creditNoteNumber.Value), utcNow);

        var cancelResult = invoice.Cancel(creditNote.Id, CreditNoteReason.OrderCancelled, utcNow);
        if (cancelResult.IsFailed)
        {
            throw new InvalidOperationException(
                "Invoice.Cancel failed in seed: " + string.Join("; ", cancelResult.Errors.Select(e => e.Message)));
        }

        _db.CreditNotes.Add(creditNote);
        await _db.SaveChangesAsync();
        return creditNote;
    }

    private Invoice BuildDraftInvoice(Guid buyerId)
    {
        var utcNow = _time.GetUtcNow();
        var line = InvoiceLine.Create(
            lineNumber: 1,
            sku: Sku.Create("SKU-FUNC-001").Value,
            description: "Functional test product",
            quantity: 1,
            unitPrice: Money.Create(123.45m, CurrencyCode.FromName("EUR")).Value,
            vatRate: VatRate.Create(0m).Value).Value;

        return Invoice.Create(
            buyerId: buyerId,
            orderId: Guid.CreateVersion7(),
            paymentId: Guid.CreateVersion7(),
            correlationId: Guid.CreateVersion7(),
            billingAddress: BuildBillingAddress(),
            lines: [line],
            vatLines: [],
            deliveryChannel: DeliveryChannel.None,
            utcNow: utcNow).Value;
    }

    private Invoice BuildIssuedInvoice(Guid buyerId, Guid orderId)
    {
        var utcNow = _time.GetUtcNow();
        var line = InvoiceLine.Create(
            lineNumber: 1,
            sku: Sku.Create("SKU-FUNC-001").Value,
            description: "Functional test product",
            quantity: 1,
            unitPrice: Money.Create(123.45m, CurrencyCode.FromName("EUR")).Value,
            vatRate: VatRate.Create(0m).Value).Value;

        var invoice = Invoice.Create(
            buyerId: buyerId,
            orderId: orderId,
            paymentId: Guid.CreateVersion7(),
            correlationId: Guid.CreateVersion7(),
            billingAddress: BuildBillingAddress(),
            lines: [line],
            vatLines: [],
            deliveryChannel: DeliveryChannel.None,
            utcNow: utcNow).Value;

        var number = InvoiceNumber.Create(utcNow.Year, _nextInvoiceSeq++).Value;
        invoice.AssignInvoiceNumber(number);
        var issueResult = invoice.Issue(BuildPdfBlobRef(number.Value), utcNow);
        if (issueResult.IsFailed)
        {
            throw new InvalidOperationException(
                "Invoice.Issue failed in seed: " + string.Join("; ", issueResult.Errors.Select(e => e.Message)));
        }

        return invoice;
    }
}
