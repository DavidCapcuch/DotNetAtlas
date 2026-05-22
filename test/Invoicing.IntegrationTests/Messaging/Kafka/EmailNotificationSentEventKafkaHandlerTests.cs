using AwesomeAssertions;
using AwesomeAssertions.Execution;
using Invoicing.Application.Common.Data;
using Invoicing.Domain.Invoices;
using Invoicing.Domain.Invoices.ValueObjects;
using Invoicing.Infrastructure.Messaging.Kafka.Notifications;
using Invoicing.Infrastructure.Persistence.Database;
using Invoicing.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Email;
using NSubstitute;
using Xunit;

namespace Invoicing.IntegrationTests.Messaging.Kafka;

[Collection(nameof(IntegrationTestCollection))]
public sealed class EmailNotificationSentEventKafkaHandlerTests
{
    private readonly IntegrationTestFixture _fixture;

    public EmailNotificationSentEventKafkaHandlerTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.ResetOutboxSubstitute();
    }

    [Fact]
    public async Task Handle_InvoicingPrefixedTemplate_TransitionsInvoiceToDelivered_AndEnqueuesAvroEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var (invoiceId, buyerId) = await _fixture.SeedIssuedInvoiceAsync(ct);

        // SeedIssuedInvoiceAsync records InvoiceIssuedEvent on the substitute; clear
        // so the assertion below only sees calls from the handler under test.
        _fixture.ResetOutboxSubstitute();

        await using var scope = _fixture.CreateScope();

        // Wire the outbox stub's Database to the real DbContext so EnsureTransactionAsync
        // can open a real Postgres transaction. Mirrors the Notifications integration test pattern.
        var dbContext = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        _fixture.OutboxSubstitute.Database.Returns(dbContext.Database);

        var handler = scope.ServiceProvider.GetRequiredService<EmailNotificationSentEventKafkaHandler>();
        var ctx = TestKafkaMessageContext.Create(ct);
        var sent = new EmailNotificationSentEvent
        {
            UserId = buyerId,
            TemplateId = "invoicing.invoice-delivered",
            IdempotencyKey = $"invoice-delivered-{invoiceId}-1",
            SentAtUtc = DateTime.UtcNow,
            OccurredOnUtc = DateTime.UtcNow,
        };

        await handler.Handle(ctx, sent);

        using var _ = new AssertionScope();

        // Invoice should be in Delivered status.
        await using var assertScope = _fixture.CreateScope();
        var db = assertScope.ServiceProvider.GetRequiredService<IInvoicingDbContext>();
        var invoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId, ct);
        invoice.Status.Should().Be(InvoiceStatus.Delivered);

        // InvoiceDeliveredOutboxPublisherDomainEventHandler fires via domain-event dispatch
        // and calls AddOutboxMessage on the outbox stub.
        _fixture.OutboxSubstitute.Received(1).AddOutboxMessage(
            "invoicing.invoices",
            buyerId.ToString(),
            Arg.Is<global::Invoicing.Invoices.InvoiceDeliveredEvent>(e =>
                e.InvoiceId == invoiceId));
    }

    [Fact]
    public async Task Handle_NonInvoicingPrefix_NoOps()
    {
        var ct = TestContext.Current.CancellationToken;
        var (invoiceId, buyerId) = await _fixture.SeedIssuedInvoiceAsync(ct);

        // SeedIssuedInvoiceAsync records InvoiceIssuedEvent on the substitute; clear
        // so DidNotReceiveWithAnyArgs below only sees calls from the handler under test.
        _fixture.ResetOutboxSubstitute();

        await using var scope = _fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        _fixture.OutboxSubstitute.Database.Returns(dbContext.Database);

        var handler = scope.ServiceProvider.GetRequiredService<EmailNotificationSentEventKafkaHandler>();

        await handler.Handle(TestKafkaMessageContext.Create(ct), new EmailNotificationSentEvent
        {
            UserId = buyerId,
            TemplateId = "weather.alert",
            IdempotencyKey = $"alert-{Guid.CreateVersion7()}-1",
            SentAtUtc = DateTime.UtcNow,
            OccurredOnUtc = DateTime.UtcNow,
        });

        // Invoice status is unchanged — handler returned early before touching the aggregate.
        await using var assert = _fixture.CreateScope();
        var db = assert.ServiceProvider.GetRequiredService<IInvoicingDbContext>();
        var invoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId, ct);
        invoice.Status.Should().Be(InvoiceStatus.Issued);

        // No outbox messages were enqueued.
        _fixture.OutboxSubstitute.DidNotReceiveWithAnyArgs().AddOutboxMessage(default!, default, default!);
    }

    [Fact]
    public async Task Handle_InvoiceAlreadyDelivered_NoOpsAndDoesNotEnqueueDuplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        var (invoiceId, buyerId) = await _fixture.SeedDeliveredInvoiceAsync(ct);

        // The seed already triggered one AddOutboxMessage call; reset before the second attempt.
        _fixture.ResetOutboxSubstitute();

        await using var scope = _fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        _fixture.OutboxSubstitute.Database.Returns(dbContext.Database);

        var handler = scope.ServiceProvider.GetRequiredService<EmailNotificationSentEventKafkaHandler>();

        await handler.Handle(TestKafkaMessageContext.Create(ct), new EmailNotificationSentEvent
        {
            UserId = buyerId,
            TemplateId = "invoicing.invoice-delivered",
            IdempotencyKey = $"invoice-delivered-{invoiceId}-2",
            SentAtUtc = DateTime.UtcNow,
            OccurredOnUtc = DateTime.UtcNow,
        });

        // Invoice remains Delivered; no second outbox message enqueued.
        await using var assert = _fixture.CreateScope();
        var db = assert.ServiceProvider.GetRequiredService<IInvoicingDbContext>();
        var invoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId, ct);
        invoice.Status.Should().Be(InvoiceStatus.Delivered);

        // Deliver was a no-op so no AddOutboxMessage should have been called.
        _fixture.OutboxSubstitute.DidNotReceiveWithAnyArgs().AddOutboxMessage(default!, default, default!);
    }

    [Fact]
    public async Task Handle_UnknownInvoiceId_ThrowsDataIntegrityException()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var scope = _fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        _fixture.OutboxSubstitute.Database.Returns(dbContext.Database);

        var handler = scope.ServiceProvider.GetRequiredService<EmailNotificationSentEventKafkaHandler>();

        var unknownInvoiceId = Guid.CreateVersion7();
        var act = async () => await handler.Handle(TestKafkaMessageContext.Create(ct), new EmailNotificationSentEvent
        {
            UserId = Guid.CreateVersion7(),
            TemplateId = "invoicing.invoice-delivered",
            IdempotencyKey = $"invoice-delivered-{unknownInvoiceId}-1",
            SentAtUtc = DateTime.UtcNow,
            OccurredOnUtc = DateTime.UtcNow,
        });

        await act.Should().ThrowAsync<Platform.SharedKernel.Exceptions.DataIntegrityException>()
            .Where(ex => ex.ErrorCode == "Invoicing.InvoiceUnknownOnDeliveryConfirmation");
    }
}
