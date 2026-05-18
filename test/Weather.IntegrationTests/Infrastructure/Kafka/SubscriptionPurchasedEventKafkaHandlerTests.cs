using FluentResults;
using KafkaFlow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Platform.CQRS;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Errors;
using Weather.Alerts;
using Weather.Application.Common.Data;
using Weather.Application.Common.Messaging;
using Weather.Application.WeatherAlerts.PurchaseSubscription;
using Weather.Infrastructure.Messaging.Kafka.Subscriptions;
using Weather.IntegrationTests.Common;
using SubscriptionTier = Weather.Alerts.SubscriptionTier;

namespace Weather.IntegrationTests.Infrastructure.Kafka;

[Collection<IntegrationTestCollection>]
public class ActivateSubscriptionCommandKafkaHandlerTests : BaseIntegrationTest
{
    private readonly ActivateSubscriptionCommandKafkaHandler _activateSubscriptionCommandKafkaHandler;

    public ActivateSubscriptionCommandKafkaHandlerTests(IntegrationTestFixture app)
        : base(app)
    {
        _activateSubscriptionCommandKafkaHandler = Scope.ServiceProvider
            .GetRequiredService<ActivateSubscriptionCommandKafkaHandler>();
    }

    [Fact]
    public async Task Handle_WhenValidCommandReceived_ShouldCreateSubscriberAndActivateSubscription()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var correlationId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();
        const int monthDurationDays = 30;
        var requestedAtUtc = DateTime.UtcNow;

        var activateSubscriptionCommand = new ActivateAlertSubscriptionCommand
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            DurationDays = monthDurationDays,
            Tier = SubscriptionTier.Pro,
            RequestedAtUtc = requestedAtUtc
        };

        var messageContext = Substitute.For<IMessageContext>();
        var messageHeaders = new MessageHeaders();
        messageContext.Headers.Returns(messageHeaders);

        var consumerContext = Substitute.For<IConsumerContext>();
        consumerContext.WorkerStopped.Returns(TestContext.Current.CancellationToken);
        messageContext.ConsumerContext.Returns(consumerContext);

        // Act
        await _activateSubscriptionCommandKafkaHandler.Handle(messageContext, activateSubscriptionCommand);

        // Assert
        var alertSubscriber = await WeatherDbContext.AlertSubscribers
            .FirstOrDefaultAsync(s => s.UserId == userId, TestContext.Current.CancellationToken);

        var outboxMessage = await WeatherDbContext.OutboxMessages
            .FirstOrDefaultAsync(
                m => m.Type == typeof(AlertSubscriptionActivatedEvent).FullName,
                TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            alertSubscriber.Should().NotBeNull();
            alertSubscriber.SubscriptionTier.Should().Be(Domain.Alerts.ValueObjects.SubscriptionTier.Pro);
            alertSubscriber.SubscriptionExpiryAtUtc.Should()
                .BeCloseTo(requestedAtUtc.AddDays(monthDurationDays), TimeSpan.FromSeconds(1));

            outboxMessage.Should().NotBeNull("SubscriptionActivatedEvent should be added to outbox on success");
            outboxMessage.KafkaKey.Should().Be(activateSubscriptionCommand.CorrelationId.ToString());
        }
    }

    [Fact]
    public async Task Handle_WhenSubscriberExists_ShouldUpgradeSubscription()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        await SetupPersistedFreeAlertSubscriberAsync(userId);

        var correlationId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();
        const int yearDurationDays = 365;
        var requestedAtUtc = DateTime.UtcNow;

        var activateSubscriptionCommand = new ActivateAlertSubscriptionCommand
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            DurationDays = yearDurationDays,
            Tier = SubscriptionTier.Ultra,
            RequestedAtUtc = requestedAtUtc
        };

        var messageContext = Substitute.For<IMessageContext>();
        var messageHeaders = new MessageHeaders();
        messageContext.Headers.Returns(messageHeaders);

        var consumerContext = Substitute.For<IConsumerContext>();
        consumerContext.WorkerStopped.Returns(TestContext.Current.CancellationToken);
        messageContext.ConsumerContext.Returns(consumerContext);

        // Act
        await _activateSubscriptionCommandKafkaHandler.Handle(messageContext, activateSubscriptionCommand);

        // Assert
        var alertSubscriber = await WeatherDbContext.AlertSubscribers
            .FirstOrDefaultAsync(s => s.UserId == userId, TestContext.Current.CancellationToken);

        var outboxMessage = await WeatherDbContext.OutboxMessages
            .FirstOrDefaultAsync(
                m => m.Type == typeof(AlertSubscriptionActivatedEvent).FullName,
                TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            alertSubscriber.Should().NotBeNull();
            alertSubscriber.SubscriptionTier.Should().Be(Domain.Alerts.ValueObjects.SubscriptionTier.Ultra);
            alertSubscriber.SubscriptionExpiryAtUtc.Should()
                .BeCloseTo(requestedAtUtc.AddDays(yearDurationDays), TimeSpan.FromSeconds(1));

            outboxMessage.Should().NotBeNull("SubscriptionActivatedEvent should be added to outbox on upgrade");
            outboxMessage.KafkaKey.Should().Be(activateSubscriptionCommand.CorrelationId.ToString());
        }
    }

    [Fact]
    public async Task Handle_WhenCommandHandlerFails_ShouldAddActivationFailedEventToOutbox()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var correlationId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();
        const int monthDurationDays = 30;
        const SubscriptionTier tier = SubscriptionTier.Pro;
        var requestedAtUtc = DateTime.UtcNow;

        var commandMessage = new ActivateAlertSubscriptionCommand
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            DurationDays = monthDurationDays,
            Tier = tier,
            RequestedAtUtc = requestedAtUtc
        };

        var messageContext = Substitute.For<IMessageContext>();
        var headers = new MessageHeaders();
        messageContext.Headers.Returns(headers);

        var consumerContext = Substitute.For<IConsumerContext>();
        consumerContext.WorkerStopped.Returns(TestContext.Current.CancellationToken);
        messageContext.ConsumerContext.Returns(consumerContext);

        // Mock the command handler to return a failure
        var mockCommandHandler =
            Substitute.For<ICommandHandler<PurchaseSubscriptionCommand>>();
        var testError = new ValidationError("TestProperty", "Test error message", "Test.ErrorCode");
        mockCommandHandler
            .HandleAsync(Arg.Any<PurchaseSubscriptionCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail(testError));

        var weatherDbTransactionalOutbox =
            Scope.ServiceProvider.GetRequiredService<ITransactionalOutbox<IWeatherDbContext>>();

        var handlerWithMock = new ActivateSubscriptionCommandKafkaHandler(
            mockCommandHandler,
            TimeProvider.System,
            Scope.ServiceProvider.GetRequiredService<ILogger<ActivateSubscriptionCommandKafkaHandler>>(),
            weatherDbTransactionalOutbox,
            Scope.ServiceProvider.GetRequiredService<IOptions<TopicsOptions>>());

        // Act
        await handlerWithMock.Handle(messageContext, commandMessage);

        // Assert
        var outboxMessage = await WeatherDbContext.OutboxMessages
            .FirstOrDefaultAsync(
                m => m.Type == typeof(AlertSubscriptionActivationFailedEvent).FullName,
                TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            outboxMessage.Should().NotBeNull();
            outboxMessage.KafkaKey.Should().Be(correlationId.ToString());
        }
    }

    private async Task SetupPersistedFreeAlertSubscriberAsync(Guid userId)
    {
        var subscriber = Domain.Alerts.AlertSubscriber.CreateFree(userId, TimeProvider.System.GetUtcNow());
        subscriber.PopDomainEvents(); // Don't dispatch creation events

        WeatherDbContext.AlertSubscribers.Add(subscriber);
        await WeatherDbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
