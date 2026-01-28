using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.Application.WeatherAlerts.ExtendSubscription;
using DotNetAtlas.Domain.Alerts;
using DotNetAtlas.Domain.Alerts.ValueObjects;
using DotNetAtlas.IntegrationTests.Common;
using DotNetAtlas.SharedKernel.Errors;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace DotNetAtlas.IntegrationTests.Application.WeatherAlerts;

[Collection<ForecastTestCollection>]
public class ExtendSubscriptionCommandHandlerTests : BaseIntegrationTest
{
    private readonly FakeTimeProvider _fakeTimeProvider = new();
    private readonly ExtendSubscriptionCommandHandler _handler;

    public ExtendSubscriptionCommandHandlerTests(IntegrationTestFixture app)
        : base(app)
    {
        _handler = new ExtendSubscriptionCommandHandler(
            Scope.ServiceProvider.GetRequiredService<IWeatherDbContext>(),
            _fakeTimeProvider,
            Scope.ServiceProvider.GetRequiredService<ILogger<ExtendSubscriptionCommandHandler>>());
    }

    [Fact]
    public async Task WhenValidRequest_ExtendsSubscription()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var utcNow = _fakeTimeProvider.GetUtcNow();
        const int initialDurationDays = 10;
        var originalExpiry = utcNow.AddDays(initialDurationDays);
        var subscriber = await SetupPersistedAlertSubscriberAsync(userId, SubscriptionTier.Pro, initialDurationDays, utcNow);

        var extendSubscriptionCommand = new ExtendSubscriptionCommand
        {
            UserId = userId,
            CorrelationId = Guid.CreateVersion7(),
            PaymentTransactionId = Guid.CreateVersion7(),
            DurationExtendedDays = 30,
            OccurredOnUtc = utcNow
        };

        // Act
        var extendSubscriptionResult = await _handler.HandleAsync(
            extendSubscriptionCommand,
            TestContext.Current.CancellationToken);

        // Assert
        var updatedSubscriber = await WeatherDbContext.AlertSubscribers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            extendSubscriptionResult.Should().BeSuccess();
            updatedSubscriber.Should().NotBeNull();
            updatedSubscriber.SubscriptionExpiryAtUtc.Should().Be(originalExpiry.AddDays(30));
        }
    }

    [Fact]
    public async Task WhenSubscriberNotFound_ReturnsFailure()
    {
        // Arrange
        var nonExistentUserId = Guid.CreateVersion7();
        var utcNow = _fakeTimeProvider.GetUtcNow();
        var extendSubscriptionCommand = new ExtendSubscriptionCommand
        {
            UserId = nonExistentUserId,
            CorrelationId = Guid.CreateVersion7(),
            PaymentTransactionId = Guid.CreateVersion7(),
            DurationExtendedDays = 30,
            OccurredOnUtc = utcNow
        };

        // Act
        var extendSubscriptionResult = await _handler.HandleAsync(
            extendSubscriptionCommand,
            TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            extendSubscriptionResult.Should().BeFailure();
            extendSubscriptionResult.Errors.Should().ContainSingle()
                .Which.Should().BeOfType<NotFoundError>();
        }
    }

    [Fact]
    public async Task WhenExpiredSubscription_ExtendsFromCurrentTime()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var utcNow = _fakeTimeProvider.GetUtcNow();
        // Create subscription that expired 5 days ago (created 6 days ago with 1 day duration)
        var pastTime = utcNow.AddDays(-6);
        await SetupPersistedAlertSubscriberAsync(userId, SubscriptionTier.Pro, 1, pastTime);

        var extendSubscriptionCommand = new ExtendSubscriptionCommand
        {
            UserId = userId,
            CorrelationId = Guid.CreateVersion7(),
            PaymentTransactionId = Guid.CreateVersion7(),
            DurationExtendedDays = 30,
            OccurredOnUtc = utcNow
        };

        // Act
        var extendSubscriptionResult = await _handler.HandleAsync(
            extendSubscriptionCommand,
            TestContext.Current.CancellationToken);

        // Assert
        var updatedSubscriber = await WeatherDbContext.AlertSubscribers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            extendSubscriptionResult.Should().BeSuccess();
            updatedSubscriber!.SubscriptionExpiryAtUtc.Should().Be(utcNow.AddDays(30));
        }
    }

    private async Task<AlertSubscriber> SetupPersistedAlertSubscriberAsync(
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

    // Note: Idempotency is now handled at the InboxMiddleware level in the KafkaFlow pipeline,
    // not at the command handler level. See SubscriptionExtendedEventKafkaHandlerTests for
    // idempotency integration tests.
}
