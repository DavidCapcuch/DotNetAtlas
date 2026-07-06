using AwesomeAssertions;
using AwesomeAssertions.Execution;
using Invoicing.Application.Common.Messaging;
using Invoicing.Application.Common.Notifications;
using Invoicing.Application.Outbox;
using Invoicing.Domain.Invoices.Events;
using Invoicing.Domain.Invoices.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Notifications;
using NSubstitute;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.ValueObjects;
using Xunit;

namespace Invoicing.UnitTests.Application.Invoices.Delivery;

public sealed class InvoiceDeliveryRequestedOutboxPublisherTests
{
    [Fact]
    public async Task Handle_QueuesNotifyUserCommand_WithCorrectTopicKeyAndPayload()
    {
        // Arrange
        var buyerId = Guid.CreateVersion7();
        var invoiceId = Guid.CreateVersion7();
        var notificationId = Guid.CreateVersion7();
        var invoiceNumber = InvoiceNumber.Create(2026, 42).Value;
        var total = Money.Create(152.00m, CurrencyCode.Eur).Value;

        var outbox = Substitute.For<ITransactionalOutbox<Invoicing.Application.Common.Data.IInvoicingDbContext>>();
        var topics = Options.Create(new TopicsOptions
        {
            Invoices = "invoicing.invoices",
            OrderingOrders = "n/a",
            PaymentsTransactions = "n/a",
            NotificationsNotifyCommands = "notifications.notify-commands",
            NotificationsNotifyEvents = "notifications.notify-events",
            DltTopicSuffix = ".DLT",
        });
        var portal = Options.Create(new BuyerPortalOptions { BaseUrl = "https://invoicing.example.com" });
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 22, 10, 0, 0, TimeSpan.Zero));

        var handler = new InvoiceDeliveryRequestedOutboxPublisherDomainEventHandler(
            outbox,
            topics,
            portal,
            clock,
            NullLogger<InvoiceDeliveryRequestedOutboxPublisherDomainEventHandler>.Instance);

        var domainEvent = new InvoiceDeliveryRequestedDomainEvent
        {
            InvoiceId = invoiceId,
            BuyerId = buyerId,
            NotificationId = notificationId,
            Channel = DeliveryChannel.Email,
            InvoiceNumber = invoiceNumber,
            Total = total,
            OccurredOnUtc = clock.GetUtcNow(),
        };

        // Act
        await handler.Handle(domainEvent, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            outbox.Received(1).AddOutboxMessage(
                "notifications.notify-commands",
                buyerId.ToString(),
                Arg.Is<NotifyUserCommand>(c =>
                    c.NotificationId == notificationId &&
                    c.RecipientUserId == buyerId &&
                    c.TemplateKey == "invoicing.invoice-delivered" &&
                    c.Payload["InvoiceNumber"] == "INV-2026-000042" &&
                    c.Payload["TotalAmount"] == "152.00" &&
                    c.Payload["Currency"] == "EUR" &&
                    c.Payload["ViewInvoiceUrl"] == $"https://invoicing.example.com/invoices/{invoiceId}"));
        }
    }
}
