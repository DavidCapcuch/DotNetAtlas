using Invoicing.Domain.Common.ValueObjects;
using Invoicing.Domain.Invoices;
using Invoicing.Domain.Invoices.Events;
using Invoicing.Domain.Invoices.ValueObjects;
using Invoicing.UnitTests.Common;
using Platform.SharedKernel.Exceptions;
using Platform.SharedKernel.ValueObjects;

namespace Invoicing.UnitTests.Invoices;

/// <summary>
/// Covers <c>Invoice</c> aggregate invariants I-1..I-6 per [invoicing.md \u00a7 2.1].
/// </summary>
public class InvoiceInvariantsTests
{
    [Fact]
    public void I1_Create_ComputesTotalFromSubtotalAndVatLines()
    {
        var line = TestDataFactory.BuildLine(quantity: 2, unitPrice: 100m); // LineTotal = 200
        var vat = TestDataFactory.BuildVatLine(baseAmount: 200m, taxAmount: 42m);

        var invoice = TestDataFactory.BuildDraftInvoice(lines: [line], vatLines: [vat]);

        invoice.Subtotal.Amount.Should().Be(200m);
        invoice.Total.Amount.Should().Be(242m);
    }

    [Fact]
    public void I1_Create_WithMultipleVatLines_SumsCorrectly()
    {
        var line1 = TestDataFactory.BuildLine(lineNumber: 1, quantity: 1, unitPrice: 200m);
        var line2 = TestDataFactory.BuildLine(lineNumber: 2, quantity: 1, unitPrice: 100m, vatRate: 0m);
        var vat21 = TestDataFactory.BuildVatLine(rate: 21m, baseAmount: 200m, taxAmount: 42m);
        var vat0 = TestDataFactory.BuildVatLine(rate: 0m, baseAmount: 100m, taxAmount: 0m);

        var invoice = TestDataFactory.BuildDraftInvoice(lines: [line1, line2], vatLines: [vat21, vat0]);

        invoice.Subtotal.Amount.Should().Be(300m);
        invoice.Total.Amount.Should().Be(342m);
    }

    [Fact]
    public void I2_Create_WithEmptyLines_ThrowsDataIntegrityException()
    {
        var act = () => Invoice.Create(
            buyerId: Guid.CreateVersion7(),
            orderId: Guid.CreateVersion7(),
            paymentId: Guid.CreateVersion7(),
            billingAddress: TestDataFactory.DefaultBillingAddress(),
            lines: [],
            vatLines: [],
            deliveryChannel: DeliveryChannel.Email,
            utcNow: TestDataFactory.FixedUtcNow);

        act.Should().Throw<DataIntegrityException>()
            .Which.ErrorCode.Should().Be("Invoicing.EmptyLines");
    }

    [Fact]
    public void I2_Create_WithMixedLineCurrencies_ThrowsDataIntegrityException()
    {
        var eur = TestDataFactory.BuildLine(lineNumber: 1, currency: "EUR");
        var usd = TestDataFactory.BuildLine(lineNumber: 2, currency: "USD");

        var act = () => Invoice.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            TestDataFactory.DefaultBillingAddress(),
            [eur, usd],
            [TestDataFactory.BuildVatLine()],
            DeliveryChannel.Email,
            TestDataFactory.FixedUtcNow);

        act.Should().Throw<DataIntegrityException>()
            .Which.ErrorCode.Should().Be("Invoicing.MixedCurrency");
    }

    [Fact]
    public void I3_Issue_StampsInvoiceNumberImmutably()
    {
        var invoice = TestDataFactory.BuildDraftInvoice();
        var number = InvoiceNumber.Create(2026, 142).Value;
        var pdf = MakePdf();

        var issued = invoice.Issue(number, pdf, TestDataFactory.FixedUtcNow);

        issued.IsSuccess.Should().BeTrue();
        invoice.InvoiceNumber.Should().Be(number);
        invoice.Status.Should().Be(InvoiceStatus.Issued);
    }

    [Fact]
    public void I4_Issue_SetsPdfBlobRef()
    {
        var invoice = TestDataFactory.BuildDraftInvoice();
        var pdf = MakePdf();

        invoice.Issue(InvoiceNumber.Create(2026, 1).Value, pdf, TestDataFactory.FixedUtcNow);

        invoice.PdfBlobRef.Should().Be(pdf);
    }

    [Fact]
    public void I5_Draft_CannotTransitionDirectlyToDelivered()
    {
        var invoice = TestDataFactory.BuildDraftInvoice();

        var result = invoice.Deliver(TestDataFactory.FixedUtcNow);

        result.IsSuccess.Should().BeFalse();
        invoice.Status.Should().Be(InvoiceStatus.Draft);
    }

    [Fact]
    public void I5_Issued_CannotTransitionToArchivedDirectly()
    {
        var invoice = TestDataFactory.BuildIssuedInvoice();

        var result = invoice.Archive();

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void I6_Cancel_WithEmptyCreditNoteId_Throws()
    {
        var invoice = TestDataFactory.BuildIssuedInvoice();

        var act = () => invoice.Cancel(Guid.Empty, CreditNoteReason.OrderCancelled, TestDataFactory.FixedUtcNow);

        act.Should().Throw<DataIntegrityException>()
            .Which.ErrorCode.Should().Be("Invoicing.InvalidCreditNoteIdOnCancel");
    }

    [Fact]
    public void I6_Cancel_StampsCancellationInfoWithCreditNoteId()
    {
        var invoice = TestDataFactory.BuildIssuedInvoice();
        var creditNoteId = Guid.CreateVersion7();

        var result = invoice.Cancel(creditNoteId, CreditNoteReason.OrderCancelled, TestDataFactory.FixedUtcNow);

        result.IsSuccess.Should().BeTrue();
        invoice.Status.Should().Be(InvoiceStatus.Cancelled);
        invoice.CancellationInfo.Should().NotBeNull();
        invoice.CancellationInfo!.CreditNoteId.Should().Be(creditNoteId);
        invoice.CancellationInfo.Reason.Should().Be(CreditNoteReason.OrderCancelled);
    }

    [Fact]
    public void Issue_RaisesInvoiceIssuedAndDeliveryRequestedWhenChannelNotNone()
    {
        var invoice = TestDataFactory.BuildDraftInvoice(deliveryChannel: DeliveryChannel.Email);
        invoice.PopDomainEvents(); // discard InvoiceCreatedDomainEvent

        invoice.Issue(InvoiceNumber.Create(2026, 1).Value, MakePdf(), TestDataFactory.FixedUtcNow);
        var events = invoice.PopDomainEvents();

        events.OfType<InvoiceIssuedDomainEvent>().Should().ContainSingle();

        // ADR-0031: a NotificationId is minted, carried on the event, and persisted on the aggregate
        // (same save) so the delivery confirmation can correlate by delivery_notification_id.
        var delivery = events.OfType<InvoiceDeliveryRequestedDomainEvent>().Should().ContainSingle().Subject;
        delivery.NotificationId.Should().NotBeEmpty();
        invoice.DeliveryNotificationId.Should().Be(delivery.NotificationId);
    }

    [Fact]
    public void Issue_WithChannelNone_DoesNotRaiseDeliveryRequested()
    {
        var invoice = TestDataFactory.BuildDraftInvoice(deliveryChannel: DeliveryChannel.None);
        invoice.PopDomainEvents();

        invoice.Issue(InvoiceNumber.Create(2026, 1).Value, MakePdf(), TestDataFactory.FixedUtcNow);
        var events = invoice.PopDomainEvents();

        events.OfType<InvoiceIssuedDomainEvent>().Should().ContainSingle();
        events.OfType<InvoiceDeliveryRequestedDomainEvent>().Should().BeEmpty();
    }

    [Fact]
    public void I3_I4_Issue_Twice_IsRejectedAndDoesNotOverwrite()
    {
        var invoice = TestDataFactory.BuildIssuedInvoice();
        var originalNumber = invoice.InvoiceNumber;
        var originalPdf = invoice.PdfBlobRef;

        var replacementNumber = InvoiceNumber.Create(2026, 999).Value;
        var replacementPdf = PdfBlobRef.Create(
            "2026/05/INV-2026-000999.pdf",
            new string('d', 64),
            sizeBytes: 4096).Value;

        var result = invoice.Issue(replacementNumber, replacementPdf, TestDataFactory.FixedUtcNow);

        result.IsSuccess.Should().BeFalse();
        invoice.InvoiceNumber.Should().Be(originalNumber);
        invoice.PdfBlobRef.Should().Be(originalPdf);
    }

    [Fact]
    public void I4_Issue2Arg_RejectsWhenPdfBlobRefAlreadySet()
    {
        // Symmetry with CreditNote.Issue(PdfBlobRef, …) — the 2-arg overload must
        // declare I-4 explicitly so a rehydrated Draft row carrying a stale pdf_blob_uri
        // cannot silently overwrite the existing blob ref. The FSM gate alone passes
        // when Status == Draft regardless of PdfBlobRef state.
        var invoice = TestDataFactory.BuildDraftInvoice();
        invoice.AssignInvoiceNumber(InvoiceNumber.Create(2026, 1).Value);

        var prePersistedPdf = PdfBlobRef.Create(
            "2026/05/INV-2026-000001.pdf",
            new string('e', 64),
            sizeBytes: 512).Value;
        typeof(Invoice)
            .GetProperty(nameof(Invoice.PdfBlobRef))!
            .SetValue(invoice, prePersistedPdf);

        var freshPdf = PdfBlobRef.Create(
            "2026/05/INV-2026-000001-fresh.pdf",
            new string('f', 64),
            sizeBytes: 1024).Value;

        var act = () => invoice.Issue(freshPdf, TestDataFactory.FixedUtcNow);

        act.Should().Throw<DataIntegrityException>()
            .Which.ErrorCode.Should().Be("Invoicing.InvoiceAlreadyIssued");
        invoice.PdfBlobRef.Should().Be(prePersistedPdf);
        invoice.Status.Should().Be(InvoiceStatus.Draft);
    }

    [Fact]
    public void HappyPath_Draft_Issued_Delivered_Archived()
    {
        var invoice = TestDataFactory.BuildDraftInvoice();

        invoice.Issue(InvoiceNumber.Create(2026, 1).Value, MakePdf(), TestDataFactory.FixedUtcNow).IsSuccess.Should().BeTrue();
        invoice.Deliver(TestDataFactory.FixedUtcNow).IsSuccess.Should().BeTrue();
        invoice.Archive().IsSuccess.Should().BeTrue();

        invoice.Status.Should().Be(InvoiceStatus.Archived);
        invoice.DeliveredAtUtc.Should().Be(TestDataFactory.FixedUtcNow);
    }

    private static PdfBlobRef MakePdf() =>
        PdfBlobRef.Create(
            "2026/05/INV-2026-000142.pdf",
            new string('b', 64),
            sizeBytes: 2048).Value;
}
