using AwesomeAssertions;
using AwesomeAssertions.Execution;
using Invoicing.Application.Common.Data;
using Invoicing.Application.Common.Messaging;
using Invoicing.Application.Outbox;
using Invoicing.Domain.Invoices.Events;
using Invoicing.Domain.Invoices.ValueObjects;
using Invoicing.Invoices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Platform.ReliableMessaging.Outbox.EFCore;
using Xunit;

namespace Invoicing.UnitTests.Application.Invoices.Delivery;

public sealed class InvoiceDeliveredOutboxPublisherTests
{
    [Fact]
    public void Handle_QueuesInvoiceDeliveredEventOnInvoicesTopic_WithBuyerIdKey()
    {
        // Arrange
        var invoiceId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();
        var correlationId = Guid.CreateVersion7();
        var deliveredAtUtc = new DateTimeOffset(2026, 5, 22, 14, 30, 0, TimeSpan.Zero);
        var occurredOnUtc = new DateTimeOffset(2026, 5, 22, 14, 30, 1, TimeSpan.Zero);

        var outbox = Substitute.For<ITransactionalOutbox<IInvoicingDbContext>>();
        var topics = Options.Create(new TopicsOptions
        {
            Invoices = "invoicing.invoices",
            OrderingOrders = "n/a",
            PaymentsTransactions = "n/a",
            NotificationsEmailCommands = "n/a",
            NotificationsEmailEvents = "n/a",
            DltTopicSuffix = ".DLT",
        });

        var handler = new InvoiceDeliveredOutboxPublisherDomainEventHandler(
            outbox,
            topics,
            NullLogger<InvoiceDeliveredOutboxPublisherDomainEventHandler>.Instance);

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
        handler.Handle(domainEvent, CancellationToken.None);

        // Assert
        using var _ = new AssertionScope();
        outbox.Received(1).AddOutboxMessage(
            "invoicing.invoices",
            buyerId.ToString(),
            Arg.Is<InvoiceDeliveredEvent>(e =>
                e.InvoiceId == invoiceId &&
                e.BuyerId == buyerId &&
                e.DeliveredAtUtc == deliveredAtUtc.UtcDateTime &&
                e.Channel == DeliveryChannel.Email.Name &&
                e.CorrelationId == correlationId &&
                e.OccurredOnUtc == occurredOnUtc.UtcDateTime));
    }
}
