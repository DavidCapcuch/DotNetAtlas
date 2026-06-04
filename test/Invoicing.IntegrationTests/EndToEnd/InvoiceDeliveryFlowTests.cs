using AwesomeAssertions;
using AwesomeAssertions.Execution;
using Invoicing.Application.Common.Data;
using Invoicing.Application.Invoices.IssueInvoice;
using Invoicing.Domain.Invoices.ValueObjects;
using Invoicing.Infrastructure.Messaging.Kafka.Notifications;
using Invoicing.Infrastructure.Persistence.Database;
using Invoicing.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notifications;
using NSubstitute;
using Platform.CQRS;
using Xunit;

namespace Invoicing.IntegrationTests.EndToEnd;

[Collection<IntegrationTestCollection>]
public sealed class InvoiceDeliveryFlowTests
{
    private readonly IntegrationTestFixture _fixture;

    public InvoiceDeliveryFlowTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.ResetOutboxSubstitute();
    }

    [Fact]
    public async Task IssueInvoice_To_InvoiceDeliveredEvent_RoundTrips_WithSimulatedNotificationsAck()
    {
        var ct = TestContext.Current.CancellationToken;
        var orderId = Guid.CreateVersion7();
        var paymentId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();

        await _fixture.SeedConvergedPendingInvoiceAsync(
            TimeProvider.System, orderId, paymentId, buyerId, totalAmount: 152.00m, currency: "EUR", ct);

        // 1) IssueInvoice
        Guid invoiceId;
        await using (var s = _fixture.CreateScope())
        {
            var handler = s.ServiceProvider.GetRequiredService<ICommandHandler<IssueInvoiceCommand, Guid>>();
            var result = await handler.HandleAsync(new IssueInvoiceCommand { OrderId = orderId }, ct);
            result.IsSuccess.Should().BeTrue();
            invoiceId = result.Value;
        }

        var notificationId = await _fixture.GetDeliveryNotificationIdAsync(invoiceId, ct);

        // 2) Assert: InvoiceIssuedEvent + NotifyUserCommand outbox rows.
        //    The OutboxSubstitute captures all AddOutboxMessage calls — verify both fired.
        using (new AssertionScope())
        {
            _fixture.OutboxSubstitute.Received().AddOutboxMessage(
                "invoicing.invoices",
                buyerId.ToString(),
                Arg.Any<global::Invoicing.Invoices.InvoiceIssuedEvent>());

            _fixture.OutboxSubstitute.Received().AddOutboxMessage(
                "notifications.notify-commands",
                buyerId.ToString(),
                Arg.Is<NotifyUserCommand>(c =>
                    c.RecipientUserId == buyerId
                    && c.TemplateKey == "invoicing.invoice-delivered"
                    && c.NotificationId == notificationId));
        }

        // 3) Simulate the Notifications BC delivery ack by invoking the Invoicing-side handler directly,
        //    correlating on the NotificationId the invoice minted at issuance (ADR-0031).
        await using (var s = _fixture.CreateScope())
        {
            // Wire the outbox stub's Database to the real DbContext so EnsureTransactionAsync
            // can open a real Postgres transaction.
            var dbContext = s.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            _fixture.OutboxSubstitute.Database.Returns(dbContext.Database);

            var handler = s.ServiceProvider.GetRequiredService<NotificationDeliveryStatusChangedEventKafkaHandler>();
            await handler.Handle(TestKafkaMessageContext.Create(ct: ct), new NotificationDeliveryStatusChangedEvent
            {
                NotificationId = notificationId,
                RecipientUserId = buyerId,
                TemplateKey = "invoicing.invoice-delivered",
                Channel = "Email",
                Status = NotificationDeliveryStatus.Dispatched,
                OccurredOnUtc = DateTime.UtcNow,
            });
        }

        // 4) Assert: invoice in Delivered + InvoiceDeliveredEvent outbox row.
        using (new AssertionScope())
        {
            await using var s = _fixture.CreateScope();
            var db = s.ServiceProvider.GetRequiredService<IInvoicingDbContext>();
            var invoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId, ct);
            invoice.Status.Should().Be(InvoiceStatus.Delivered);

            _fixture.OutboxSubstitute.Received().AddOutboxMessage(
                "invoicing.invoices",
                buyerId.ToString(),
                Arg.Is<global::Invoicing.Invoices.InvoiceDeliveredEvent>(e =>
                    e.InvoiceId == invoiceId && e.Channel == "Email"));
        }
    }
}
