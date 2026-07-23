using Invoicing.Application.Outbox;
using Invoicing.Domain.Common.ValueObjects;
using Invoicing.Domain.CreditNotes.Events;
using Invoicing.Domain.CreditNotes.ValueObjects;
using Invoicing.Domain.Invoices.ValueObjects;
using Platform.SharedKernel.ValueObjects;

namespace Invoicing.UnitTests.Application.Outbox;

/// <summary>
/// Field-level mapping guard for <see cref="CreditNoteIssuedMapper"/> — the reference ids, the two
/// formatted numbers, the timestamps, the reason, and the Pdf fields. The negative monetary
/// <c>Total</c> (sign, precision, scale) is owned by <see cref="OutboxMoneyMappingTests"/> and is
/// not re-asserted here.
/// </summary>
public sealed class CreditNoteIssuedMapperTests
{
    private static readonly DateTimeOffset IssueDate = new(2026, 4, 24, 9, 15, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OccurredOn = new(2026, 4, 24, 9, 15, 3, TimeSpan.Zero);

    [Fact]
    public void ToCreditNoteIssuedEvent_MapsScalarAndReferenceFields()
    {
        // Arrange — CreditNoteId/OriginalInvoiceId/BuyerId are distinct so an id cross-wire flips an
        // assertion; the two numbers are distinct strings so a CreditNoteNumber↔OriginalInvoiceNumber
        // swap (or emitting the value object over its .Value) is caught; the instants differ so an
        // IssueDate↔OccurredOnUtc swap is caught. Reason uses a non-default value (Adjustment, reachable
        // in v2) so the assertion kills both emitting the int Value and hardcoding the v1 OrderCancelled.
        var creditNoteId = Guid.CreateVersion7();
        var originalInvoiceId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();

        var domainEvent = new CreditNoteIssuedDomainEvent
        {
            CreditNoteId = creditNoteId,
            CreditNoteNumber = CreditNoteNumber.Create(2026, 7).Value,
            OriginalInvoiceId = originalInvoiceId,
            OriginalInvoiceNumber = InvoiceNumber.Create(2026, 142).Value,
            BuyerId = buyerId,
            IssueDate = IssueDate,
            Total = Money.Create(-242.00m, CurrencyCode.Eur).Value,
            Reason = CreditNoteReason.Adjustment,
            PdfBlobRef = PdfBlobRef.Create("2026/04/CN-2026-000007.pdf", new string('b', 64), sizeBytes: 4096).Value,
            OccurredOnUtc = OccurredOn,
        };

        // Act
        var avro = domainEvent.ToCreditNoteIssuedEvent();

        // Assert
        using (new AssertionScope())
        {
            avro.CreditNoteId.Should().Be(creditNoteId);
            avro.CreditNoteNumber.Should().Be("CN-2026-000007");
            avro.OriginalInvoiceId.Should().Be(originalInvoiceId);
            avro.OriginalInvoiceNumber.Should().Be("INV-2026-000142");
            avro.BuyerId.Should().Be(buyerId);
            avro.IssueDate.Should().Be(IssueDate.UtcDateTime);
            avro.Reason.Should().Be(CreditNoteReason.Adjustment.Name);
            avro.PdfBlobName.Should().Be("2026/04/CN-2026-000007.pdf");
            avro.PdfContentHash.Should().Be(new string('b', 64));
            avro.PdfSizeBytes.Should().Be(4096);
            avro.OccurredOnUtc.Should().Be(OccurredOn.UtcDateTime);
        }
    }
}
