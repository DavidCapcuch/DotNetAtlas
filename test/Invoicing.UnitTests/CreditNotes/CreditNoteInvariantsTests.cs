using Invoicing.Domain.Common.ValueObjects;
using Invoicing.Domain.CreditNotes;
using Invoicing.Domain.CreditNotes.Events;
using Invoicing.Domain.CreditNotes.ValueObjects;
using Invoicing.Domain.Invoices.ValueObjects;
using Invoicing.UnitTests.Common;
using Platform.SharedKernel.Exceptions;

namespace Invoicing.UnitTests.CreditNotes;

/// <summary>
/// Covers <c>CreditNote</c> aggregate invariants I-CN-1..I-CN-3. I-CN-1 is enforced on the
/// source side (<c>Invoice.ToReversalSnapshot</c>) per the snapshot refactor — tests
/// targeting cancelled / draft invoices exercise that surface.
/// </summary>
public class CreditNoteInvariantsTests
{
    [Fact]
    public void ICN1_ToReversalSnapshot_FromCancelledInvoice_Throws()
    {
        var invoice = TestDataFactory.BuildIssuedInvoice();
        var creditNoteId = Guid.CreateVersion7();
        invoice.Cancel(creditNoteId, CreditNoteReason.OrderCancelled, TestDataFactory.FixedUtcNow);

        var act = () => invoice.ToReversalSnapshot(TestDataFactory.FixedUtcNow);

        act.Should().Throw<DataIntegrityException>()
            .Which.ErrorCode.Should().Be("Invoicing.SnapshotFromIneligibleInvoice");
    }

    [Fact]
    public void ICN1_ToReversalSnapshot_FromDraftInvoice_Throws()
    {
        var invoice = TestDataFactory.BuildDraftInvoice();

        var act = () => invoice.ToReversalSnapshot(TestDataFactory.FixedUtcNow);

        act.Should().Throw<DataIntegrityException>()
            .Which.ErrorCode.Should().Be("Invoicing.SnapshotFromIneligibleInvoice");
    }

    [Fact]
    public void ICN2_Create_ProducesNegativeTotal()
    {
        var invoice = TestDataFactory.BuildIssuedInvoice();
        var originalTotal = invoice.Total.Amount;

        var creditNote = CreditNote.Create(
            invoice.ToReversalSnapshot(TestDataFactory.FixedUtcNow),
            CreditNoteReason.OrderCancelled,
            TestDataFactory.FixedUtcNow).Value;

        using (new AssertionScope())
        {
            creditNote.Total.Amount.Should().Be(-originalTotal);
            creditNote.Total.Currency.Should().Be(invoice.Total.Currency);
        }
    }

    [Fact]
    public void ICN2_Create_LinesSignsFlipped()
    {
        var invoice = TestDataFactory.BuildIssuedInvoice();
        var originalLine = invoice.Lines[0];

        var creditNote = CreditNote.Create(
            invoice.ToReversalSnapshot(TestDataFactory.FixedUtcNow),
            CreditNoteReason.OrderCancelled,
            TestDataFactory.FixedUtcNow).Value;

        var creditLine = creditNote.Lines[0];
        using (new AssertionScope())
        {
            creditLine.LineTotal.Amount.Should().Be(-originalLine.LineTotal.Amount);
            creditLine.UnitPrice.Amount.Should().Be(-originalLine.UnitPrice.Amount);
            creditLine.Quantity.Should().Be(originalLine.Quantity);
        }
    }

    [Fact]
    public void ICN3_Issue_StampsCreditNoteNumberImmutably()
    {
        var invoice = TestDataFactory.BuildIssuedInvoice();
        var creditNote = CreditNote.Create(
            invoice.ToReversalSnapshot(TestDataFactory.FixedUtcNow),
            CreditNoteReason.OrderCancelled,
            TestDataFactory.FixedUtcNow).Value;

        var number = CreditNoteNumber.Create(2026, 8).Value;
        var pdf = MakePdf();

        var result = creditNote.Issue(number, pdf, TestDataFactory.FixedUtcNow);

        using (new AssertionScope())
        {
            result.IsSuccess.Should().BeTrue();
            creditNote.CreditNoteNumber.Should().Be(number);
            creditNote.PdfBlobRef.Should().Be(pdf);
            creditNote.IssueDate.Should().Be(TestDataFactory.FixedUtcNow);
        }
    }

    [Fact]
    public void ICN3_Issue_Twice_ThrowsDataIntegrityException()
    {
        var invoice = TestDataFactory.BuildIssuedInvoice();
        var creditNote = CreditNote.Create(
            invoice.ToReversalSnapshot(TestDataFactory.FixedUtcNow),
            CreditNoteReason.OrderCancelled,
            TestDataFactory.FixedUtcNow).Value;

        creditNote.Issue(CreditNoteNumber.Create(2026, 8).Value, MakePdf(), TestDataFactory.FixedUtcNow);

        var act = () => creditNote.Issue(CreditNoteNumber.Create(2026, 9).Value, MakePdf(), TestDataFactory.FixedUtcNow);

        act.Should().Throw<DataIntegrityException>()
            .Which.ErrorCode.Should().Be("Invoicing.CreditNoteAlreadyIssued");
    }

    [Fact]
    public void Create_RaisesCreditNoteCreatedEvent()
    {
        var invoice = TestDataFactory.BuildIssuedInvoice();

        var creditNote = CreditNote.Create(
            invoice.ToReversalSnapshot(TestDataFactory.FixedUtcNow),
            CreditNoteReason.OrderCancelled,
            TestDataFactory.FixedUtcNow).Value;

        creditNote.PopDomainEvents().OfType<CreditNoteCreatedDomainEvent>().Should().ContainSingle();
    }

    [Fact]
    public void Issue_RaisesCreditNoteIssuedEvent()
    {
        var invoice = TestDataFactory.BuildIssuedInvoice();
        var creditNote = CreditNote.Create(
            invoice.ToReversalSnapshot(TestDataFactory.FixedUtcNow),
            CreditNoteReason.OrderCancelled,
            TestDataFactory.FixedUtcNow).Value;
        creditNote.PopDomainEvents(); // discard created event

        creditNote.Issue(CreditNoteNumber.Create(2026, 1).Value, MakePdf(), TestDataFactory.FixedUtcNow);

        creditNote.PopDomainEvents().OfType<CreditNoteIssuedDomainEvent>().Should().ContainSingle();
    }

    [Fact]
    public void HappyPath_Issued_Delivered_Archived()
    {
        var invoice = TestDataFactory.BuildIssuedInvoice();
        var creditNote = CreditNote.Create(
            invoice.ToReversalSnapshot(TestDataFactory.FixedUtcNow),
            CreditNoteReason.OrderCancelled,
            TestDataFactory.FixedUtcNow).Value;
        creditNote.Issue(CreditNoteNumber.Create(2026, 1).Value, MakePdf(), TestDataFactory.FixedUtcNow);

        creditNote.Deliver(TestDataFactory.FixedUtcNow).IsSuccess.Should().BeTrue();
        creditNote.Archive().IsSuccess.Should().BeTrue();

        creditNote.Status.Should().Be(CreditNoteStatus.Archived);
    }

    private static PdfBlobRef MakePdf() =>
        PdfBlobRef.Create(
            "2026/05/CN-2026-000008.pdf",
            new string('c', 64),
            sizeBytes: 2048).Value;
}
