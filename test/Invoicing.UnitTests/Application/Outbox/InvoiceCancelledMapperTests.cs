using Invoicing.Application.Outbox;
using Invoicing.Domain.Common.ValueObjects;
using Invoicing.Domain.Invoices.Events;

namespace Invoicing.UnitTests.Application.Outbox;

/// <summary>
/// Field-level mapping guard for <see cref="InvoiceCancelledMapper"/> — a pure status transition
/// with no money, so (unlike its sibling events) nothing is delegated to
/// <see cref="OutboxMoneyMappingTests"/>. Reason asserts the SmartEnum .Name, not its int Value.
/// </summary>
public sealed class InvoiceCancelledMapperTests
{
    [Fact]
    public void ToInvoiceCancelledEvent_MapsAllFields()
    {
        // Arrange — InvoiceId/BuyerId/CreditNoteId are distinct and the two instants differ, so any
        // field cross-wire flips an assertion.
        var invoiceId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();
        var creditNoteId = Guid.CreateVersion7();
        var cancelledAtUtc = new DateTimeOffset(2026, 4, 24, 9, 15, 0, TimeSpan.Zero);
        var occurredOnUtc = new DateTimeOffset(2026, 4, 24, 9, 15, 3, TimeSpan.Zero);

        var domainEvent = new InvoiceCancelledDomainEvent
        {
            InvoiceId = invoiceId,
            BuyerId = buyerId,
            CancelledAtUtc = cancelledAtUtc,
            Reason = CreditNoteReason.OrderCancelled,
            CreditNoteId = creditNoteId,
            OccurredOnUtc = occurredOnUtc,
        };

        // Act
        var avro = domainEvent.ToInvoiceCancelledEvent();

        // Assert
        using (new AssertionScope())
        {
            avro.InvoiceId.Should().Be(invoiceId);
            avro.BuyerId.Should().Be(buyerId);
            avro.CancelledAtUtc.Should().Be(cancelledAtUtc.UtcDateTime);
            avro.Reason.Should().Be(CreditNoteReason.OrderCancelled.Name);
            avro.CreditNoteId.Should().Be(creditNoteId);
            avro.OccurredOnUtc.Should().Be(occurredOnUtc.UtcDateTime);
        }
    }
}
