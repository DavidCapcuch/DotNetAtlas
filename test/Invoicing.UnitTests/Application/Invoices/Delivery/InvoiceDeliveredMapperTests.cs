using AwesomeAssertions;
using AwesomeAssertions.Execution;
using Invoicing.Application.Outbox;
using Invoicing.Domain.Invoices.Events;
using Invoicing.Domain.Invoices.ValueObjects;
using Xunit;

namespace Invoicing.UnitTests.Application.Invoices.Delivery;

public sealed class InvoiceDeliveredMapperTests
{
    [Fact]
    public void ToInvoiceDeliveredEvent_MapsAllFields()
    {
        // Arrange
        var invoiceId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();
        var correlationId = Guid.CreateVersion7();
        var deliveredAtUtc = new DateTimeOffset(2026, 5, 22, 14, 30, 0, TimeSpan.Zero);
        var occurredOnUtc = new DateTimeOffset(2026, 5, 22, 14, 30, 1, TimeSpan.Zero);

        var domainEvent = new InvoiceDeliveredDomainEvent
        {
            InvoiceId = invoiceId,
            BuyerId = buyerId,
            DeliveredAtUtc = deliveredAtUtc,
            Channel = DeliveryChannel.Email,
            CorrelationId = correlationId,
            OccurredOnUtc = occurredOnUtc,
        };

        // Act
        var avroEvent = domainEvent.ToInvoiceDeliveredEvent();

        // Assert
        using var _ = new AssertionScope();
        avroEvent.InvoiceId.Should().Be(invoiceId);
        avroEvent.BuyerId.Should().Be(buyerId);
        avroEvent.DeliveredAtUtc.Should().Be(deliveredAtUtc.UtcDateTime);
        avroEvent.Channel.Should().Be(DeliveryChannel.Email.Name);
        avroEvent.CorrelationId.Should().Be(correlationId);
        avroEvent.OccurredOnUtc.Should().Be(occurredOnUtc.UtcDateTime);
    }
}
