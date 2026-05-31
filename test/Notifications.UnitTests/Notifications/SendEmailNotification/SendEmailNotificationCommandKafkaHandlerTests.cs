using AwesomeAssertions;
using FluentResults;
using KafkaFlow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Notifications.Application.Common.Data;
using Notifications.Application.Common.Messaging;
using Notifications.Application.Email;
using Notifications.Infrastructure.Email;
using Notifications.Infrastructure.SendEmailNotification;
using NSubstitute;
using Platform.ReliableMessaging.Outbox.EFCore;
using Xunit;

namespace Notifications.UnitTests.Notifications.SendEmailNotification;

public sealed class SendEmailNotificationCommandKafkaHandlerTests : IDisposable
{
    private readonly ITransactionalOutbox<INotificationsDbContext> _outbox =
        Substitute.For<ITransactionalOutbox<INotificationsDbContext>>();

    private readonly IEmailGateway _gateway = Substitute.For<IEmailGateway>();
    private readonly IEmailTemplateRenderer _renderer = new EmailTemplateRenderer();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 5, 22, 10, 0, 0, TimeSpan.Zero));
    private readonly DbContext _fakeDbContext = new InMemoryDbContextStub();

    public SendEmailNotificationCommandKafkaHandlerTests()
    {
        // EnsureTransactionAsync is a static extension on DatabaseFacade — wire a real
        // in-memory DatabaseFacade so the extension method can execute the lambda.
        // Real transactional semantics are covered by integration tests.
        _outbox.Database.Returns(_fakeDbContext.Database);
    }

    public void Dispose() => _fakeDbContext.Dispose();

    private SendEmailNotificationCommandKafkaHandler CreateHandler() =>
        new(_outbox, _gateway, _renderer,
            Options.Create(new TopicsOptions
            {
                EmailCommands = "notifications.email-commands",
                EmailEvents = "notifications.email-events",
                DltTopicSuffix = ".DLT",
            }),
            _clock,
            NullLogger<SendEmailNotificationCommandKafkaHandler>.Instance);

    private static IMessageContext CreateMessageContext()
    {
        var ctx = Substitute.For<IMessageContext>();
        var consumerCtx = Substitute.For<IConsumerContext>();
        consumerCtx.WorkerStopped.Returns(CancellationToken.None);
        ctx.ConsumerContext.Returns(consumerCtx);
        return ctx;
    }

    private sealed class InMemoryDbContextStub : DbContext
    {
        public InMemoryDbContextStub()
            : base(new DbContextOptionsBuilder()
                .UseInMemoryDatabase(Guid.CreateVersion7().ToString())
                .ConfigureWarnings(b => b.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options)
        {
        }
    }

    [Fact]
    public async Task Handle_HappyPath_SendsEmail_AndQueuesSentEvent()
    {
        _gateway.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok());

        var userId = Guid.CreateVersion7();
        var cmd = new global::Notifications.Email.SendEmailNotificationCommand
        {
            UserId = userId,
            TemplateId = "invoicing.invoice-delivered",
            TemplateData = new Dictionary<string, string>
            {
                ["InvoiceNumber"] = "INV-2026-000142",
                ["ViewInvoiceUrl"] = "https://invoicing.example.com/invoices/00000000-0000-0000-0000-000000000001",
            },
            IdempotencyKey = "invoice-delivered-00000000-0000-0000-0000-000000000001-1",
            OccurredOnUtc = _clock.GetUtcNow().UtcDateTime,
        };

        await CreateHandler().Handle(CreateMessageContext(), cmd);

        await _gateway.Received(1).SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
        _outbox.Received(1).AddOutboxMessage(
            "notifications.email-events",
            cmd.UserId.ToString(),
            Arg.Is<global::Notifications.Email.EmailNotificationSentEvent>(e =>
                e.UserId == cmd.UserId &&
                e.TemplateId == cmd.TemplateId &&
                e.IdempotencyKey == cmd.IdempotencyKey));
    }

    [Fact]
    public async Task Handle_GatewayFailure_ThrowsForKafkaFlowRetry()
    {
        _gateway.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail("smtp down"));

        var act = async () => await CreateHandler().Handle(
            CreateMessageContext(),
            new global::Notifications.Email.SendEmailNotificationCommand
            {
                UserId = Guid.CreateVersion7(),
                TemplateId = "invoicing.invoice-delivered",
                TemplateData = new Dictionary<string, string>
                {
                    ["InvoiceNumber"] = "INV-2026-000001",
                    ["ViewInvoiceUrl"] = "https://x/",
                },
                IdempotencyKey = "invoice-delivered-x-1",
                OccurredOnUtc = _clock.GetUtcNow().UtcDateTime,
            });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*smtp down*");
    }
}
