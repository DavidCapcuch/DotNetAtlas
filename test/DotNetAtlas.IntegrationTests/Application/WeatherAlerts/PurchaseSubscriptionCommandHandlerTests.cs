using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.Application.WeatherAlerts.PurchaseSubscription;
using DotNetAtlas.Domain.Alerts;
using DotNetAtlas.Domain.Alerts.ValueObjects;
using DotNetAtlas.IntegrationTests.Common;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace DotNetAtlas.IntegrationTests.Application.WeatherAlerts;

[Collection<ForecastTestCollection>]
public class PurchaseSubscriptionCommandHandlerTests : BaseIntegrationTest
{
    private readonly FakeTimeProvider _fakeTimeProvider = new();
    private readonly PurchaseSubscriptionCommandHandler _purchaseSubscriptionCommandHandler;

    public PurchaseSubscriptionCommandHandlerTests(IntegrationTestFixture app)
        : base(app)
    {
        _purchaseSubscriptionCommandHandler = new PurchaseSubscriptionCommandHandler(
            Scope.ServiceProvider.GetRequiredService<IWeatherDbContext>(),
            Scope.ServiceProvider.GetRequiredService<ILogger<PurchaseSubscriptionCommandHandler>>(),
            _fakeTimeProvider);
    }

    [Fact]
    public async Task WhenNewUser_CreatesSubscriberAndUpgrades()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        const int durationDays = 30;
        var utcNow = _fakeTimeProvider.GetUtcNow();

        var purchaseSubscriptionCommand = new PurchaseSubscriptionCommand
        {
            UserId = userId,
            CorrelationId = Guid.CreateVersion7(),
            PaymentTransactionId = Guid.CreateVersion7(),
            Tier = SubscriptionTier.Pro,
            DurationDays = durationDays,
            OccurredOnUtc = utcNow
        };

        // Act
        var purchaseSubscriptionResult = await _purchaseSubscriptionCommandHandler.HandleAsync(
            purchaseSubscriptionCommand,
            TestContext.Current.CancellationToken);

        // Assert
        var subscriber = await WeatherDbContext.AlertSubscribers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            purchaseSubscriptionResult.Should().BeSuccess();
            subscriber.Should().NotBeNull();
            subscriber.SubscriptionTier.Should().Be(SubscriptionTier.Pro);
            subscriber.SubscriptionExpiryAtUtc.Should().Be(utcNow.AddDays(durationDays));
        }
    }

    [Fact]
    public async Task WhenExistingFreeUser_UpgradesToProTier()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        await SetupPersistedFreeAlertSubscriberAsync(userId);

        const int durationDays = 30;
        var utcNow = _fakeTimeProvider.GetUtcNow();

        var purchaseSubscriptionCommand = new PurchaseSubscriptionCommand
        {
            UserId = userId,
            CorrelationId = Guid.CreateVersion7(),
            PaymentTransactionId = Guid.CreateVersion7(),
            Tier = SubscriptionTier.Pro,
            DurationDays = durationDays,
            OccurredOnUtc = utcNow
        };

        // Act
        var purchaseSubscriptionResult = await _purchaseSubscriptionCommandHandler.HandleAsync(
            purchaseSubscriptionCommand,
            TestContext.Current.CancellationToken);

        // Assert
        var subscriber = await WeatherDbContext.AlertSubscribers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            purchaseSubscriptionResult.Should().BeSuccess();
            subscriber.Should().NotBeNull();
            subscriber.SubscriptionTier.Should().Be(SubscriptionTier.Pro);
            subscriber.SubscriptionExpiryAtUtc.Should().Be(utcNow.AddDays(durationDays));
        }
    }

    [Fact]
    public async Task WhenExistingProUser_UpgradesToUltraTier()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var utcNow = _fakeTimeProvider.GetUtcNow();
        // Create Pro subscriber with 10 days remaining
        await SetupPersistedPaidAlertSubscriberAsync(userId, SubscriptionTier.Pro, 10, utcNow);

        const int durationDays = 60;

        var purchaseSubscriptionCommand = new PurchaseSubscriptionCommand
        {
            UserId = userId,
            CorrelationId = Guid.CreateVersion7(),
            PaymentTransactionId = Guid.CreateVersion7(),
            Tier = SubscriptionTier.Ultra,
            DurationDays = durationDays,
            OccurredOnUtc = utcNow
        };

        // Act
        var purchaseSubscriptionResult = await _purchaseSubscriptionCommandHandler.HandleAsync(
            purchaseSubscriptionCommand,
            TestContext.Current.CancellationToken);

        // Assert
        var subscriber = await WeatherDbContext.AlertSubscribers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            purchaseSubscriptionResult.Should().BeSuccess();
            subscriber.Should().NotBeNull();
            subscriber.SubscriptionTier.Should().Be(SubscriptionTier.Ultra);
            subscriber.SubscriptionExpiryAtUtc.Should().Be(utcNow.AddDays(durationDays));
        }
    }

    [Fact]
    public async Task WhenPurchasingUltraTier_SetsCorrectMaxSubscriptions()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        const int durationDays = 30;
        var utcNow = _fakeTimeProvider.GetUtcNow();

        var purchaseSubscriptionCommand = new PurchaseSubscriptionCommand
        {
            UserId = userId,
            CorrelationId = Guid.CreateVersion7(),
            PaymentTransactionId = Guid.CreateVersion7(),
            Tier = SubscriptionTier.Ultra,
            DurationDays = durationDays,
            OccurredOnUtc = utcNow
        };

        // Act
        var purchaseSubscriptionResult = await _purchaseSubscriptionCommandHandler.HandleAsync(
            purchaseSubscriptionCommand,
            TestContext.Current.CancellationToken);

        // Assert
        var subscriber = await WeatherDbContext.AlertSubscribers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            purchaseSubscriptionResult.Should().BeSuccess();
            subscriber.Should().NotBeNull();
            subscriber.SubscriptionTier.Should().Be(SubscriptionTier.Ultra);
            subscriber.SubscriptionTier.MaxSubscriptions.Should().Be(100);
        }
    }

    private async Task<AlertSubscriber> SetupPersistedFreeAlertSubscriberAsync(Guid userId)
    {
        var subscriber = AlertSubscriber.CreateFree(userId);
        subscriber.PopDomainEvents(); // Don't dispatch creation events

        WeatherDbContext.AlertSubscribers.Add(subscriber);
        await WeatherDbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return subscriber;
    }

    private async Task<AlertSubscriber> SetupPersistedPaidAlertSubscriberAsync(
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
}
