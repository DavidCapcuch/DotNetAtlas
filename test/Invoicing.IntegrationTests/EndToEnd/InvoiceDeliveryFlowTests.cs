using AwesomeAssertions;
using AwesomeAssertions.Execution;
using Invoicing.Application.Common.Data;
using Invoicing.Application.Invoices.IssueInvoice;
using Invoicing.Domain.Invoices;
using Invoicing.Domain.Invoices.ValueObjects;
using Invoicing.Infrastructure.Messaging.Kafka.Notifications;
using Invoicing.Infrastructure.Persistence.Database;
using Invoicing.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Email;
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
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var paymentId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();

        await _fixture.SeedConvergedPendingInvoiceAsync(
            correlationId, orderId, paymentId, buyerId, totalAmount: 152.00m, currency: "EUR", ct);

        // 1) IssueInvoice
        Guid invoiceId;
        await using (var s = _fixture.CreateScope())
        {
            var handler = s.ServiceProvider.GetRequiredService<ICommandHandler<IssueInvoiceCommand, Guid>>();
            var result = await handler.HandleAsync(new IssueInvoiceCommand { CorrelationId = correlationId }, ct);
            result.IsSuccess.Should().BeTrue();
            invoiceId = result.Value;
        }

        // 2) Assert: InvoiceIssuedEvent + SendEmailNotificationCommand outbox rows.
        //    The OutboxSubstitute captures all AddOutboxMessage calls — verify both fired.
        using (new AssertionScope())
        {
            _fixture.OutboxSubstitute.Received().AddOutboxMessage(
                "invoicing.invoices",
                buyerId.ToString(),
                Arg.Any<global::Invoicing.Invoices.InvoiceIssuedEvent>());

            _fixture.OutboxSubstitute.Received().AddOutboxMessage(
                "notifications.email-commands",
                buyerId.ToString(),
                Arg.Is<SendEmailNotificationCommand>(c =>
                    c.UserId == buyerId
                    && c.TemplateId == "invoicing.invoice-delivered"
                    && c.IdempotencyKey == $"invoice-delivered-{invoiceId}-1"));
        }

        // 3) Simulate Notifications BC ack by invoking the Invoicing-side handler directly.
        await using (var s = _fixture.CreateScope())
        {
            // Wire the outbox stub's Database to the real DbContext so EnsureTransactionAsync
            // can open a real Postgres transaction — mirrors the pattern in EmailNotificationSentEventKafkaHandlerTests.
            var dbContext = s.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            _fixture.OutboxSubstitute.Database.Returns(dbContext.Database);

            var handler = s.ServiceProvider.GetRequiredService<EmailNotificationSentEventKafkaHandler>();
            var ctx = TestKafkaMessageContext.Create(ct: ct);
            await handler.Handle(ctx, new EmailNotificationSentEvent
            {
                UserId = buyerId,
                TemplateId = "invoicing.invoice-delivered",
                IdempotencyKey = $"invoice-delivered-{invoiceId}-1",
                SentAtUtc = DateTime.UtcNow,
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
