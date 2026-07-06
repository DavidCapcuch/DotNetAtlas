using AwesomeAssertions;
using AwesomeAssertions.Execution;
using Invoicing.Application.Common.Data;
using Invoicing.Domain.Invoices.ValueObjects;
using Invoicing.Infrastructure.Messaging.Kafka.Notifications;
using Invoicing.Infrastructure.Persistence.Database;
using Invoicing.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notifications;
using NSubstitute;
using Xunit;

namespace Invoicing.IntegrationTests.Messaging.Kafka;

[Collection<IntegrationTestCollection>]
public sealed class NotificationDeliveryStatusChangedEventKafkaHandlerTests
{
    private readonly IntegrationTestFixture _fixture;

    public NotificationDeliveryStatusChangedEventKafkaHandlerTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.ResetOutboxSubstitute();
    }

    [Fact]
    public async Task Handle_EmailDispatched_TransitionsInvoiceToDelivered_AndEnqueuesAvroEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var (invoiceId, buyerId) = await _fixture.SeedIssuedInvoiceAsync(TimeProvider.System, ct);
        var notificationId = await _fixture.GetDeliveryNotificationIdAsync(invoiceId, ct);

        // SeedIssuedInvoiceAsync records InvoiceIssuedEvent + NotifyUserCommand on the substitute;
        // clear so the assertion below only sees calls from the handler under test.
        _fixture.ResetOutboxSubstitute();

        await using var scope = _fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        _fixture.OutboxSubstitute.Database.Returns(dbContext.Database);

        var handler = scope.ServiceProvider.GetRequiredService<NotificationDeliveryStatusChangedEventKafkaHandler>();
        await handler.Handle(TestKafkaMessageContext.Create(ct: ct), Delivered(notificationId, buyerId));

        using (new AssertionScope())
        {
            await using var assertScope = _fixture.CreateScope();
            var db = assertScope.ServiceProvider.GetRequiredService<IInvoicingDbContext>();
            var invoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId, ct);
            invoice.Status.Should().Be(InvoiceStatus.Delivered);

            _fixture.OutboxSubstitute.Received(1).AddOutboxMessage(
                "invoicing.invoices",
                buyerId.ToString(),
                Arg.Is<global::Invoicing.Invoices.InvoiceDeliveredEvent>(e => e.InvoiceId == invoiceId));
        }
    }

    [Fact]
    public async Task Handle_FailedStatus_NoOps()
    {
        var ct = TestContext.Current.CancellationToken;
        var (invoiceId, buyerId) = await _fixture.SeedIssuedInvoiceAsync(TimeProvider.System, ct);
        var notificationId = await _fixture.GetDeliveryNotificationIdAsync(invoiceId, ct);
        _fixture.ResetOutboxSubstitute();

        await using var scope = _fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        _fixture.OutboxSubstitute.Database.Returns(dbContext.Database);

        var handler = scope.ServiceProvider.GetRequiredService<NotificationDeliveryStatusChangedEventKafkaHandler>();
        await handler.Handle(TestKafkaMessageContext.Create(ct: ct), new NotificationDeliveryStatusChangedEvent
        {
            NotificationId = notificationId,
            RecipientUserId = buyerId,
            TemplateKey = "invoicing.invoice-delivered",
            Channel = "Email",
            Status = NotificationDeliveryStatus.Failed,
            OccurredOnUtc = DateTime.UtcNow,
        });

        await using var assert = _fixture.CreateScope();
        var db = assert.ServiceProvider.GetRequiredService<IInvoicingDbContext>();
        var invoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId, ct);
        invoice.Status.Should().Be(InvoiceStatus.Issued);
        _fixture.OutboxSubstitute.DidNotReceiveWithAnyArgs().AddOutboxMessage(default!, default, default!);
    }

    [Fact]
    public async Task Handle_NonInvoicingTemplate_NoOps()
    {
        var ct = TestContext.Current.CancellationToken;
        var (invoiceId, buyerId) = await _fixture.SeedIssuedInvoiceAsync(TimeProvider.System, ct);
        var notificationId = await _fixture.GetDeliveryNotificationIdAsync(invoiceId, ct);
        _fixture.ResetOutboxSubstitute();

        await using var scope = _fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        _fixture.OutboxSubstitute.Database.Returns(dbContext.Database);

        var handler = scope.ServiceProvider.GetRequiredService<NotificationDeliveryStatusChangedEventKafkaHandler>();
        await handler.Handle(TestKafkaMessageContext.Create(ct: ct), new NotificationDeliveryStatusChangedEvent
        {
            NotificationId = notificationId,
            RecipientUserId = buyerId,
            TemplateKey = "order.shipped",
            Channel = "Email",
            Status = NotificationDeliveryStatus.Dispatched,
            OccurredOnUtc = DateTime.UtcNow,
        });

        await using var assert = _fixture.CreateScope();
        var db = assert.ServiceProvider.GetRequiredService<IInvoicingDbContext>();
        var invoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId, ct);
        invoice.Status.Should().Be(InvoiceStatus.Issued);
        _fixture.OutboxSubstitute.DidNotReceiveWithAnyArgs().AddOutboxMessage(default!, default, default!);
    }

    [Fact]
    public async Task Handle_InvoiceAlreadyDelivered_NoOpsAndDoesNotEnqueueDuplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        var (invoiceId, buyerId) = await _fixture.SeedDeliveredInvoiceAsync(TimeProvider.System, ct);
        var notificationId = await _fixture.GetDeliveryNotificationIdAsync(invoiceId, ct);
        _fixture.ResetOutboxSubstitute();

        await using var scope = _fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        _fixture.OutboxSubstitute.Database.Returns(dbContext.Database);

        var handler = scope.ServiceProvider.GetRequiredService<NotificationDeliveryStatusChangedEventKafkaHandler>();
        await handler.Handle(TestKafkaMessageContext.Create(ct: ct), Delivered(notificationId, buyerId));

        await using var assert = _fixture.CreateScope();
        var db = assert.ServiceProvider.GetRequiredService<IInvoicingDbContext>();
        var invoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId, ct);
        invoice.Status.Should().Be(InvoiceStatus.Delivered);
        _fixture.OutboxSubstitute.DidNotReceiveWithAnyArgs().AddOutboxMessage(default!, default, default!);
    }

    [Fact]
    public async Task Handle_UnknownNotificationId_ThrowsDataIntegrityException()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var scope = _fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        _fixture.OutboxSubstitute.Database.Returns(dbContext.Database);

        var handler = scope.ServiceProvider.GetRequiredService<NotificationDeliveryStatusChangedEventKafkaHandler>();
        var act = async () => await handler.Handle(
            TestKafkaMessageContext.Create(ct: ct),
            Delivered(Guid.CreateVersion7(), Guid.CreateVersion7()));

        await act.Should().ThrowAsync<Platform.SharedKernel.Exceptions.DataIntegrityException>()
            .Where(ex => ex.ErrorCode == "Invoicing.InvoiceUnknownOnDeliveryConfirmation");
    }

    private static NotificationDeliveryStatusChangedEvent Delivered(Guid notificationId, Guid recipientUserId) => new()
    {
        NotificationId = notificationId,
        RecipientUserId = recipientUserId,
        TemplateKey = "invoicing.invoice-delivered",
        Channel = "Email",
        Status = NotificationDeliveryStatus.Dispatched,
        OccurredOnUtc = DateTime.UtcNow,
    };
}
