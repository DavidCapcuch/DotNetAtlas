using KafkaFlow;
using Microsoft.Extensions.Logging.Abstractions;
using Notifications.Application.Dispatch;
using Notifications.Domain.Channels;
using Notifications.Infrastructure.NotifyUser;
using NSubstitute;
using Xunit;

namespace Notifications.UnitTests.NotifyUser;

public sealed class NotifyUserCommandKafkaHandlerTests
{
    private readonly IChannelDispatchEnqueuer _enqueuer = Substitute.For<IChannelDispatchEnqueuer>();

    [Fact]
    public async Task Handle_EnqueuesExactlyOneEmailDispatch_CarryingTheCommandFields()
    {
        var handler = new NotifyUserCommandKafkaHandler(_enqueuer, NullLogger<NotifyUserCommandKafkaHandler>.Instance);

        var notificationId = Guid.CreateVersion7();
        var recipientUserId = Guid.CreateVersion7();
        var cmd = new NotifyUserCommand
        {
            NotificationId = notificationId,
            RecipientUserId = recipientUserId,
            TemplateKey = "invoicing.invoice-delivered",
            Payload = new Dictionary<string, string> { ["InvoiceNumber"] = "INV-2026-000042" },
            OccurredOnUtc = new DateTime(2026, 6, 4, 0, 0, 0, DateTimeKind.Utc),
        };

        await handler.Handle(Substitute.For<IMessageContext>(), cmd);

        _enqueuer.Received(1).Enqueue(
            ChannelType.Email,
            Arg.Is<NotificationDispatch>(d =>
                d.NotificationId == notificationId
                && d.RecipientUserId == recipientUserId
                && d.TemplateKey == "invoicing.invoice-delivered"
                && d.Payload["InvoiceNumber"] == "INV-2026-000042"));
    }

    [Fact]
    public async Task Handle_EnqueueFailure_PropagatesSoTheInboxRollsBack()
    {
        _enqueuer
            .When(e => e.Enqueue(Arg.Any<ChannelType>(), Arg.Any<NotificationDispatch>()))
            .Do(_ => throw new InvalidOperationException("scheduler down"));

        var handler = new NotifyUserCommandKafkaHandler(_enqueuer, NullLogger<NotifyUserCommandKafkaHandler>.Instance);

        var cmd = new NotifyUserCommand
        {
            NotificationId = Guid.CreateVersion7(),
            RecipientUserId = Guid.CreateVersion7(),
            TemplateKey = "invoicing.invoice-delivered",
            Payload = new Dictionary<string, string>(),
            OccurredOnUtc = new DateTime(2026, 6, 4, 0, 0, 0, DateTimeKind.Utc),
        };

        var act = () => handler.Handle(Substitute.For<IMessageContext>(), cmd);

        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }
}
