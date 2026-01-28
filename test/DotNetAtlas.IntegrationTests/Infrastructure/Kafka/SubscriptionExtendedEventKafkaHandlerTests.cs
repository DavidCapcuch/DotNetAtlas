using DotNetAtlas.Infrastructure.Messaging.Kafka.Dev;
using DotNetAtlas.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Weather.Alerts;

namespace DotNetAtlas.IntegrationTests.Infrastructure.Kafka;

/// <summary>
/// End-to-end integration tests for ExtendSubscriptionCommand Kafka handling.
/// These tests publish messages to Kafka and verify the full middleware pipeline,
/// including the InboxMiddleware for idempotent processing.
/// </summary>
[Collection<ForecastTestCollection>]
public class ExtendSubscriptionCommandKafkaHandlerTests : BaseIntegrationTest
{
    private static readonly TimeSpan ConsumerTimeout = TimeSpan.FromSeconds(10);

    private readonly DevEventsKafkaProducer _devEventsProducer;

    public ExtendSubscriptionCommandKafkaHandlerTests(IntegrationTestFixture app)
        : base(app)
    {
        _devEventsProducer = Scope.ServiceProvider.GetRequiredService<DevEventsKafkaProducer>();
    }

    [Fact]
    public async Task Handle_WhenValidCommandReceived_ShouldExtendSubscriptionAndRecordInbox()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var correlationId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();
        const int initialDurationDays = 10;
        var utcNow = DateTimeOffset.UtcNow;
        var initialExpiry = utcNow.AddDays(initialDurationDays);
        await SetupPersistedAlertSubscriberAsync(userId, correlationId, paymentTransactionId,
            Domain.Alerts.ValueObjects.SubscriptionTier.Pro, initialDurationDays, utcNow);

        const int extensionDays = 30;
        var extendSubscriptionCommand = new ExtendSubscriptionCommand
        {
            CorrelationId = Guid.CreateVersion7(),
            UserId = userId,
            PaymentTransactionId = Guid.CreateVersion7(),
            DurationDays = extensionDays,
            RequestedAtUtc = DateTime.UtcNow
        };

        // Act - Publish to Kafka and let the consumer process it through the full middleware pipeline
        await _devEventsProducer.PublishExtendSubscriptionCommandAsync(extendSubscriptionCommand);

        // Wait for the consumer to process the message
        await WaitHelper.WaitForAsync(
            async () =>
            {
                var subscriber = await WeatherDbContext.AlertSubscribers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.UserId == userId, TestContext.Current.CancellationToken);
                return subscriber?.SubscriptionExpiryAtUtc > initialExpiry;
            },
            ConsumerTimeout,
            "Consumer did not process ExtendSubscriptionCommand within timeout");

        // Assert
        var updatedSubscriber = await WeatherDbContext.AlertSubscribers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, TestContext.Current.CancellationToken);

        var inboxMessages = await WeatherDbContext.InboxMessages
            .AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);

        var successOutboxMessage = await WeatherDbContext.OutboxMessages
            .AsNoTracking()
            .FirstOrDefaultAsync(
                m => m.Type == typeof(SubscriptionExtendedEvent).FullName,
                TestContext.Current.CancellationToken);

        var failureOutboxMessage = await WeatherDbContext.OutboxMessages
            .AsNoTracking()
            .FirstOrDefaultAsync(
                m => m.Type == typeof(SubscriptionExtensionActivationFailedEvent).FullName,
                TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            updatedSubscriber.Should().NotBeNull();
            updatedSubscriber.SubscriptionExpiryAtUtc.Should()
                .BeCloseTo(initialExpiry.AddDays(extensionDays), TimeSpan.FromSeconds(5));
            inboxMessages.Should().ContainSingle("InboxMiddleware should record the processed message");
            successOutboxMessage.Should().NotBeNull("SubscriptionExtendedEvent should be added to outbox on success");
            successOutboxMessage.KafkaKey.Should().Be(extendSubscriptionCommand.PaymentTransactionId.ToString());
            failureOutboxMessage.Should().BeNull("No failure event should be published on successful extension");
        }
    }

    [Fact]
    public async Task Handle_WhenTwoDistinctCommands_ShouldProcessBothAndExtendTwice()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var correlationId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();
        const int initialDurationDays = 10;
        var utcNow = DateTimeOffset.UtcNow;
        var initialExpiry = utcNow.AddDays(initialDurationDays);
        await SetupPersistedAlertSubscriberAsync(userId, correlationId, paymentTransactionId,
            Domain.Alerts.ValueObjects.SubscriptionTier.Pro, initialDurationDays, utcNow);

        const int extensionDays = 30;
        var firstCommand = new ExtendSubscriptionCommand
        {
            CorrelationId = Guid.CreateVersion7(),
            UserId = userId,
            PaymentTransactionId = Guid.CreateVersion7(),
            DurationDays = extensionDays,
            RequestedAtUtc = DateTime.UtcNow
        };

        // Publish first message
        await _devEventsProducer.PublishExtendSubscriptionCommandAsync(firstCommand);

        // Wait for first message to be processed
        await WaitHelper.WaitForAsync(
            async () =>
            {
                var subscriber = await WeatherDbContext.AlertSubscribers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.UserId == userId, TestContext.Current.CancellationToken);
                return subscriber?.SubscriptionExpiryAtUtc > initialExpiry;
            },
            ConsumerTimeout,
            "Consumer did not process first ExtendSubscriptionCommand within timeout");

        // Publish a second different command (new message ID) to verify both are tracked
        var secondCommand = new ExtendSubscriptionCommand
        {
            CorrelationId = Guid.CreateVersion7(),
            UserId = userId,
            PaymentTransactionId = Guid.CreateVersion7(), // Different transaction
            DurationDays = extensionDays,
            RequestedAtUtc = DateTime.UtcNow
        };
        await _devEventsProducer.PublishExtendSubscriptionCommandAsync(secondCommand);

        // Wait for the second message to be processed
        await WaitHelper.WaitForAsync(
            async () =>
            {
                var count = await WeatherDbContext.InboxMessages
                    .AsNoTracking()
                    .CountAsync(TestContext.Current.CancellationToken);
                return count == 2;
            },
            ConsumerTimeout,
            "Consumer did not process second ExtendSubscriptionCommand within timeout");

        // Assert
        var finalSubscriber = await WeatherDbContext.AlertSubscribers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, TestContext.Current.CancellationToken);

        var inboxCount = await WeatherDbContext.InboxMessages
            .AsNoTracking()
            .CountAsync(TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            inboxCount.Should().Be(2,
                "Two distinct messages (different message.id) should each be recorded in inbox");
            finalSubscriber.Should().NotBeNull();
            // Subscription was extended twice (once per unique message)
            finalSubscriber.SubscriptionExpiryAtUtc.Should()
                .BeCloseTo(initialExpiry.AddDays(extensionDays * 2), TimeSpan.FromSeconds(5),
                    "Each unique message extends the subscription");
        }
    }

    [Fact]
    public async Task Handle_WhenSameMessageIdRedelivered_ShouldBeIdempotent()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var correlationId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();
        const int initialDurationDays = 10;
        var utcNow = DateTimeOffset.UtcNow;
        var initialExpiry = utcNow.AddDays(initialDurationDays);
        await SetupPersistedAlertSubscriberAsync(userId, correlationId, paymentTransactionId,
            Domain.Alerts.ValueObjects.SubscriptionTier.Pro, initialDurationDays, utcNow);

        const int extensionDays = 30;
        var messageId = Guid.CreateVersion7(); // Same message ID for both publishes

        var extendSubscriptionCommand = new ExtendSubscriptionCommand
        {
            CorrelationId = Guid.CreateVersion7(),
            UserId = userId,
            PaymentTransactionId = Guid.CreateVersion7(),
            DurationDays = extensionDays,
            RequestedAtUtc = DateTime.UtcNow
        };

        // Act - Publish the same message twice with the same message ID (simulating Kafka redelivery)
        await _devEventsProducer.PublishExtendSubscriptionCommandWithMessageIdAsync(extendSubscriptionCommand, messageId);

        // Wait for first message to be processed
        await WaitHelper.WaitForAsync(
            async () =>
            {
                var subscriber = await WeatherDbContext.AlertSubscribers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.UserId == userId, TestContext.Current.CancellationToken);
                return subscriber?.SubscriptionExpiryAtUtc > initialExpiry;
            },
            ConsumerTimeout,
            "Consumer did not process first ExtendSubscriptionCommand within timeout");

        // Capture the expiry after first processing
        var expiryAfterFirstMessage = (await WeatherDbContext.AlertSubscribers
                .AsNoTracking()
                .FirstAsync(s => s.UserId == userId, TestContext.Current.CancellationToken))
            .SubscriptionExpiryAtUtc!.Value;

        // Publish the same message again with the same message ID (simulating Kafka redelivery)
        await _devEventsProducer.PublishExtendSubscriptionCommandWithMessageIdAsync(extendSubscriptionCommand, messageId);

        // Wait a bit to allow the second message to be processed (or skipped)
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        // Assert
        var finalSubscriber = await WeatherDbContext.AlertSubscribers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, TestContext.Current.CancellationToken);

        var inboxMessages = await WeatherDbContext.InboxMessages
            .AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            inboxMessages.Should().ContainSingle(
                "InboxMiddleware should record only one message (duplicate was skipped)");
            inboxMessages[0].MessageId.Should().Be(messageId.ToString());
            finalSubscriber.Should().NotBeNull();
            // Subscription should only be extended once (second message was idempotently skipped)
            finalSubscriber.SubscriptionExpiryAtUtc.Should()
                .BeCloseTo(expiryAfterFirstMessage, TimeSpan.FromSeconds(1),
                    "Subscription should not be extended again for duplicate message");
        }
    }

    [Fact]
    public async Task Handle_WhenSubscriberNotFound_ShouldAddExtensionActivationFailedEventToOutbox()
    {
        // Arrange
        var nonExistentUserId = Guid.CreateVersion7();
        var correlationId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();
        const int extensionDays = 30;

        var extendSubscriptionCommand = new ExtendSubscriptionCommand
        {
            CorrelationId = correlationId,
            UserId = nonExistentUserId,
            PaymentTransactionId = paymentTransactionId,
            DurationDays = extensionDays,
            RequestedAtUtc = DateTime.UtcNow
        };

        // Act
        await _devEventsProducer.PublishExtendSubscriptionCommandAsync(extendSubscriptionCommand);

        // Wait for the failure event to be added to outbox
        await WaitHelper.WaitForAsync(
            async () =>
            {
                var outboxMessage = await WeatherDbContext.OutboxMessages
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        m => m.Type == typeof(SubscriptionExtensionActivationFailedEvent).FullName,
                        TestContext.Current.CancellationToken);
                return outboxMessage != null;
            },
            ConsumerTimeout,
            "SubscriptionExtensionActivationFailedEvent was not added to outbox within timeout");

        // Assert
        var outboxMessage = await WeatherDbContext.OutboxMessages
            .AsNoTracking()
            .FirstOrDefaultAsync(
                m => m.Type == typeof(SubscriptionExtensionActivationFailedEvent).FullName,
                TestContext.Current.CancellationToken);

        var inboxCount = await WeatherDbContext.InboxMessages
            .AsNoTracking()
            .CountAsync(TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            outboxMessage.Should().NotBeNull();
            outboxMessage.KafkaKey.Should().Be(correlationId.ToString());
            inboxCount.Should().Be(1, "InboxMiddleware should still record the processed message");
        }
    }

    private async Task<Domain.Alerts.AlertSubscriber> SetupPersistedAlertSubscriberAsync(
        Guid userId,
        Guid correlationId,
        Guid paymentTransactionId,
        Domain.Alerts.ValueObjects.SubscriptionTier tier,
        int durationDays,
        DateTimeOffset utcNow)
    {
        var subscriber = Domain.Alerts.AlertSubscriber.CreateWithPaidSubscription(
            userId,
            correlationId,
            paymentTransactionId,
            tier,
            durationDays,
            utcNow);
        subscriber.PopDomainEvents(); // Don't dispatch creation events

        WeatherDbContext.AlertSubscribers.Add(subscriber);
        await WeatherDbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return subscriber;
    }
}
