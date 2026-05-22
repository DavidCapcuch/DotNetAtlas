using AwesomeAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Common.Persistence.Database;
using Notifications.Email;
using Notifications.IntegrationTests.Common;
using Notifications.Notifications.SendEmailNotification;
using NSubstitute;
using Platform.ReliableMessaging.Outbox.EFCore;
using Xunit;

namespace Notifications.IntegrationTests.Messaging.Kafka;

/// <summary>
/// Integration test for <see cref="SendEmailNotificationCommandKafkaHandler"/> exercising the
/// full handler round-trip against the Testcontainers Postgres fixture: template rendering,
/// email gateway dispatch, and outbox message enqueue.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class SendEmailNotificationCommandKafkaHandlerTests
{
    private readonly IntegrationTestFixture _fixture;

    public SendEmailNotificationCommandKafkaHandlerTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.ResetOutboxSubstitute();
    }

    [Fact]
    public async Task Handle_InvoicingInvoiceDelivered_SendsEmailAndQueuesSentEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.CreateVersion7();
        var idempotencyKey = $"invoice-delivered-{Guid.CreateVersion7()}-1";

        await using var scope = _fixture.CreateScope();

        // Wire the singleton outbox stub's Database property to the scoped DbContext's
        // DatabaseFacade so EnsureTransactionAsync can run a real Postgres transaction.
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        _fixture.OutboxSubstitute.Database.Returns(dbContext.Database);

        var handler = scope.ServiceProvider.GetRequiredService<SendEmailNotificationCommandKafkaHandler>();
        var ctx = TestKafkaMessageContext.Create(ct);

        var cmd = new SendEmailNotificationCommand
        {
            UserId = userId,
            TemplateId = "invoicing.invoice-delivered",
            TemplateData = new Dictionary<string, string>
            {
                ["InvoiceNumber"] = "INV-2026-INTEG-1",
                ["ViewInvoiceUrl"] = "https://invoicing.test/invoices/abc",
                ["TotalAmount"] = "152.00",
                ["Currency"] = "EUR",
            },
            IdempotencyKey = idempotencyKey,
            OccurredOnUtc = DateTime.UtcNow,
        };

        await handler.Handle(ctx, cmd);

        using var _ = new AssertionScope();

        var outbox = scope.ServiceProvider.GetRequiredService<ITransactionalOutbox<INotificationDbContext>>();
        outbox.Received(1).AddOutboxMessage(
            "notifications.email-events",
            userId.ToString(),
            Arg.Is<EmailNotificationSentEvent>(e =>
                e.UserId == userId &&
                e.TemplateId == cmd.TemplateId &&
                e.IdempotencyKey == idempotencyKey));
    }
}
