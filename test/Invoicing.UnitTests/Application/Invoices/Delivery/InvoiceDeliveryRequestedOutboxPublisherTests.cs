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
using Notifications.Email;
using NSubstitute;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.ValueObjects;
using Xunit;

namespace Invoicing.UnitTests.Application.Invoices.Delivery;

public sealed class InvoiceDeliveryRequestedOutboxPublisherTests
{
    [Fact]
    public async Task Handle_QueuesSendEmailNotificationCommand_WithCorrectTopicKeyAndTemplateData()
    {
        // Arrange
        var buyerId = Guid.CreateVersion7();
        var invoiceId = Guid.CreateVersion7();
        var invoiceNumber = InvoiceNumber.Create(2026, 42).Value;
        var total = new Money(152.00m, CurrencyCode.Eur);

        var outbox = Substitute.For<ITransactionalOutbox<Invoicing.Application.Common.Data.IInvoicingDbContext>>();
        var topics = Options.Create(new InvoicingTopicsOptions
        {
            Invoices = "invoicing.invoices",
            OrderingOrders = "n/a",
            PaymentsTransactions = "n/a",
            NotificationsEmailCommands = "notifications.email-commands",
            NotificationsEmailEvents = "notifications.email-events",
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
            Channel = DeliveryChannel.Email,
            Attempt = 1,
            CorrelationId = Guid.CreateVersion7(),
            InvoiceNumber = invoiceNumber,
            Total = total,
            OccurredOnUtc = clock.GetUtcNow(),
        };

        // Act
        await handler.Handle(domainEvent, TestContext.Current.CancellationToken);

        // Assert
        using var _ = new AssertionScope();
        outbox.Received(1).AddOutboxMessage(
            "notifications.email-commands",
            buyerId.ToString(),
            Arg.Is<SendEmailNotificationCommand>(c =>
                c.UserId == buyerId &&
                c.TemplateId == "invoicing.invoice-delivered" &&
                c.IdempotencyKey == $"invoice-delivered-{invoiceId}-1" &&
                c.TemplateData["InvoiceNumber"] == "INV-2026-000042" &&
                c.TemplateData["TotalAmount"] == "152.00" &&
                c.TemplateData["Currency"] == "EUR" &&
                c.TemplateData["ViewInvoiceUrl"] == $"https://invoicing.example.com/invoices/{invoiceId}"));
    }
}
