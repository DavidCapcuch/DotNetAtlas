using FluentResults.Extensions.FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Weather.Application.Common.Data;
using Weather.Application.WeatherAlerts.ProcessExpiredSubscriptions;
using Weather.Domain.Alerts;
using Weather.Domain.Alerts.ValueObjects;
using Weather.IntegrationTests.Common;

namespace Weather.IntegrationTests.Application.WeatherAlerts;

[Collection<IntegrationTestCollection>]
public class ProcessExpiredSubscriptionsCommandHandlerTests : BaseIntegrationTest
{
    private const string SendEmailNotificationCommandType = "Notifications.Email.SendEmailNotificationCommand";

    private readonly FakeTimeProvider _fakeTimeProvider = new();
    private readonly ProcessExpiredSubscriptionsCommandHandler _processExpiredSubscriptionsCommandHandler;

    public ProcessExpiredSubscriptionsCommandHandlerTests(IntegrationTestFixture app)
        : base(app)
    {
        _processExpiredSubscriptionsCommandHandler = new ProcessExpiredSubscriptionsCommandHandler(
            Scope.ServiceProvider.GetRequiredService<IWeatherDbContext>(),
            _fakeTimeProvider,
            Scope.ServiceProvider.GetRequiredService<ILogger<ProcessExpiredSubscriptionsCommandHandler>>());
    }

    [Fact]
    public async Task WhenExpiredSubscribers_DowngradesToFree()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var utcNow = _fakeTimeProvider.GetUtcNow();
        // Create subscription that expired 1 day ago (created 2 days ago with 1 day duration)
        await SetupPersistedExpiredAlertSubscriberAsync(userId, SubscriptionTier.Pro, utcNow.AddDays(-2));

        var processExpiredSubscriptionsCommand = new ProcessExpiredSubscriptionsCommand
        {
            BatchSize = 1000
        };

        // Act
        var processExpiredSubscriptionsResult = await _processExpiredSubscriptionsCommandHandler.HandleAsync(
            processExpiredSubscriptionsCommand,
            TestContext.Current.CancellationToken);

        // Assert
        var updatedSubscriber = await WeatherDbContext.AlertSubscribers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, TestContext.Current.CancellationToken);

        var outboxMessages = await WeatherDbContext.OutboxMessages
            .AsNoTracking()
            .Where(m => m.Type == SendEmailNotificationCommandType)
            .ToListAsync(TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            processExpiredSubscriptionsResult.Should().BeSuccess();
            updatedSubscriber.Should().NotBeNull();
            updatedSubscriber.SubscriptionTier.Should().Be(SubscriptionTier.Free);
            updatedSubscriber.SubscriptionExpiryAtUtc.Should().BeNull();
            outboxMessages.Should().ContainSingle();
            outboxMessages[0].Type.Should().Be(SendEmailNotificationCommandType);
        }
    }

    [Fact]
    public async Task WhenNoExpiredSubscribers_ReturnsOkWithoutEmail()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var utcNow = _fakeTimeProvider.GetUtcNow();
        // Create subscription that expires in 30 days
        await SetupPersistedActiveAlertSubscriberAsync(userId, SubscriptionTier.Pro, 30, utcNow);

        var processExpiredSubscriptionsCommand = new ProcessExpiredSubscriptionsCommand
        {
            BatchSize = 1000
        };

        // Act
        var processExpiredSubscriptionsResult = await _processExpiredSubscriptionsCommandHandler.HandleAsync(
            processExpiredSubscriptionsCommand,
            TestContext.Current.CancellationToken);

        // Assert
        var updatedSubscriber = await WeatherDbContext.AlertSubscribers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, TestContext.Current.CancellationToken);

        var outboxMessages = await WeatherDbContext.OutboxMessages
            .AsNoTracking()
            .Where(m => m.Type == SendEmailNotificationCommandType)
            .ToListAsync(TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            processExpiredSubscriptionsResult.Should().BeSuccess();
            updatedSubscriber.Should().NotBeNull();
            updatedSubscriber.SubscriptionTier.Should().Be(SubscriptionTier.Pro);
            outboxMessages.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task WhenMultipleExpiredSubscribers_ProcessesAllAndSendsEmails()
    {
        // Arrange
        var utcNow = _fakeTimeProvider.GetUtcNow();
        var userId1 = Guid.CreateVersion7();
        var userId2 = Guid.CreateVersion7();
        // Expired 1 day ago: created 2 days ago with 1 day duration
        await SetupPersistedExpiredAlertSubscriberAsync(userId1, SubscriptionTier.Pro, utcNow.AddDays(-2));
        // Expired 6 days ago: created 7 days ago with 1 day duration
        await SetupPersistedExpiredAlertSubscriberAsync(userId2, SubscriptionTier.Ultra, utcNow.AddDays(-7));

        var processExpiredSubscriptionsCommand = new ProcessExpiredSubscriptionsCommand
        {
            BatchSize = 1000
        };

        // Act
        var processExpiredSubscriptionsResult = await _processExpiredSubscriptionsCommandHandler.HandleAsync(
            processExpiredSubscriptionsCommand,
            TestContext.Current.CancellationToken);

        // Assert
        var updatedSubscribers = await WeatherDbContext.AlertSubscribers
            .AsNoTracking()
            .Where(s => s.UserId == userId1 || s.UserId == userId2)
            .ToListAsync(TestContext.Current.CancellationToken);

        var outboxMessages = await WeatherDbContext.OutboxMessages
            .AsNoTracking()
            .Where(m => m.Type == SendEmailNotificationCommandType)
            .ToListAsync(TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            processExpiredSubscriptionsResult.Should().BeSuccess();
            updatedSubscribers.Should().HaveCount(2);
            updatedSubscribers.Should().AllSatisfy(s =>
            {
                s.SubscriptionTier.Should().Be(SubscriptionTier.Free);
                s.SubscriptionExpiryAtUtc.Should().BeNull();
            });
            outboxMessages.Should().HaveCount(2);
            outboxMessages.Should().OnlyContain(m => m.Type == SendEmailNotificationCommandType);
        }
    }

    [Fact]
    public async Task WhenExpiredSubscriberHasExcessSubscriptions_RemovesThem()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var utcNow = _fakeTimeProvider.GetUtcNow();
        await SetupPersistedExpiredAlertSubscriberWithSubscriptionsAsync(userId, SubscriptionTier.Pro, utcNow.AddDays(-2), 10);

        var processExpiredSubscriptionsCommand = new ProcessExpiredSubscriptionsCommand
        {
            BatchSize = 1000
        };

        // Act
        var processExpiredSubscriptionsResult = await _processExpiredSubscriptionsCommandHandler.HandleAsync(
            processExpiredSubscriptionsCommand,
            TestContext.Current.CancellationToken);

        // Assert
        var updatedSubscriber = await WeatherDbContext.AlertSubscribers
            .AsNoTracking()
            .Include(s => s.MonitoredLocationAlertsSubscriptions)
            .FirstOrDefaultAsync(s => s.UserId == userId, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            processExpiredSubscriptionsResult.Should().BeSuccess();
            updatedSubscriber.Should().NotBeNull();
            updatedSubscriber.SubscriptionTier.Should().Be(SubscriptionTier.Free);
            updatedSubscriber.MonitoredLocationAlertsSubscriptions.Should()
                .HaveCount(SubscriptionTier.Free.MaxSubscriptions);
        }
    }

    [Fact]
    public async Task WhenFreeSubscriber_DoesNotProcess()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        await SetupPersistedFreeAlertSubscriberAsync(userId);

        var processExpiredSubscriptionsCommand = new ProcessExpiredSubscriptionsCommand
        {
            BatchSize = 1000
        };

        // Act
        var processExpiredSubscriptionsResult = await _processExpiredSubscriptionsCommandHandler.HandleAsync(
            processExpiredSubscriptionsCommand,
            TestContext.Current.CancellationToken);

        // Assert
        var outboxMessages = await WeatherDbContext.OutboxMessages
            .AsNoTracking()
            .Where(m => m.Type == SendEmailNotificationCommandType)
            .ToListAsync(TestContext.Current.CancellationToken);

        processExpiredSubscriptionsResult.Should().BeSuccess();
        outboxMessages.Should().BeEmpty();
    }

    private async Task<AlertSubscriber> SetupPersistedExpiredAlertSubscriberAsync(
        Guid userId,
        SubscriptionTier tier,
        DateTimeOffset createdAtUtc)
    {
        var subscriber = AlertSubscriber.CreateWithPaidSubscription(
            userId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            tier,
            1, // Creates an expired subscription when createdAtUtc is in the past
            createdAtUtc);
        subscriber.PopDomainEvents(); // Don't dispatch creation events

        WeatherDbContext.AlertSubscribers.Add(subscriber);
        await WeatherDbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return subscriber;
    }

    private async Task<AlertSubscriber> SetupPersistedActiveAlertSubscriberAsync(
        Guid userId,
        SubscriptionTier tier,
        int durationDays,
        DateTimeOffset utcNow)
    {
        var subscriber = AlertSubscriber.CreateWithPaidSubscription(
            userId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            tier,
            durationDays,
            utcNow);
        subscriber.PopDomainEvents(); // Don't dispatch creation events

        WeatherDbContext.AlertSubscribers.Add(subscriber);
        await WeatherDbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return subscriber;
    }

    private async Task<AlertSubscriber> SetupPersistedExpiredAlertSubscriberWithSubscriptionsAsync(
        Guid userId,
        SubscriptionTier tier,
        DateTimeOffset createdAtUtc,
        int subscriptionCount)
    {
        var subscriber = AlertSubscriber.CreateWithPaidSubscription(
            userId, Guid.CreateVersion7(), Guid.CreateVersion7(), tier,
            1, // Creates an expired subscription when createdAtUtc is in the past
            createdAtUtc);

        // Add location subscriptions
        for (var i = 0; i < subscriptionCount; i++)
        {
            subscriber.SubscribeToMonitoredLocation(Guid.CreateVersion7());
        }

        subscriber.PopDomainEvents(); // Don't dispatch creation events

        WeatherDbContext.AlertSubscribers.Add(subscriber);
        await WeatherDbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return subscriber;
    }

    private async Task<AlertSubscriber> SetupPersistedFreeAlertSubscriberAsync(Guid userId)
    {
        var subscriber = AlertSubscriber.CreateFree(userId);
        subscriber.PopDomainEvents(); // Don't dispatch creation events

        WeatherDbContext.AlertSubscribers.Add(subscriber);
        await WeatherDbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return subscriber;
    }
}
