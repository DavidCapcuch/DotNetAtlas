using Avro.Specific;
using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.Application.Common.Messaging;
using DotNetAtlas.Application.WeatherAlerts.RecordWeatherReading;
using DotNetAtlas.Domain.Alerts;
using DotNetAtlas.Domain.Alerts.Events;
using DotNetAtlas.Domain.Alerts.ValueObjects;
using DotNetAtlas.Domain.Common.ValueObjects;
using DotNetAtlas.Infrastructure.Persistence.Database;
using DotNetAtlas.ReliableMessaging.Outbox.EFCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notifications.Email;
using NSubstitute;

namespace DotNetAtlas.UnitTests.WeatherAlerts.DomainEventHandlers;

/// <summary>
/// Unit tests for WeatherAlertEmailNotificationDomainEventHandler using in-memory EF Core.
/// </summary>
public class WeatherAlertEmailNotificationDomainEventHandlerTests : IDisposable
{
    private static readonly DateTimeOffset UtcNow = DateTimeOffset.UtcNow;

    private readonly WeatherDbContext _dbContext;
    private readonly FakeTransactionalOutbox _fakeOutbox;
    private readonly WeatherAlertEmailNotificationDomainEventHandler _weatherAlertEmailNotificationDomainEventHandler;

    public WeatherAlertEmailNotificationDomainEventHandlerTests()
    {
        var options = new DbContextOptionsBuilder<WeatherDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new WeatherDbContext(options);
        _fakeOutbox = new FakeTransactionalOutbox();

        var topicsOptions = Options.Create(new TopicsOptions
        {
            ForecastRequested = "weather.forecast.requested",
            WeatherAlertSubscriptionsCommands = "weather.alert-subscriptions.commands",
            WeatherAlertSubscriptions = "weather.alerts.events",
            OrderAlertSubscriptions = "order.alert-subscription.events",
            NotificationCommands = "notifications.commands",
            PaymentCommands = "finance.payment.commands",
            Payments = "finance.payment.events",
            WeatherFeedbackEvents = "weather.feedback.events",
            DltTopicSuffix = ".DLT"
        });

        _weatherAlertEmailNotificationDomainEventHandler = new WeatherAlertEmailNotificationDomainEventHandler(
            Substitute.For<ILogger<WeatherAlertEmailNotificationDomainEventHandler>>(),
            _dbContext,
            _fakeOutbox,
            topicsOptions);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task Handle_WhenNoSubscribers_AddsNoOutboxMessages()
    {
        // Arrange
        var monitoredLocationId = Guid.CreateVersion7();
        var weatherAlertIssuedDomainEvent = CreateWeatherAlertIssuedDomainEvent(monitoredLocationId);

        // Act
        await _weatherAlertEmailNotificationDomainEventHandler.Handle(weatherAlertIssuedDomainEvent,
            CancellationToken.None);

        // Assert
        _fakeOutbox.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenOneSubscriber_AddsOneOutboxMessage()
    {
        // Arrange
        var monitoredLocationId = Guid.CreateVersion7();
        var subscriber = await CreateSubscriberWithSubscription(monitoredLocationId);
        var weatherAlertIssuedDomainEvent = CreateWeatherAlertIssuedDomainEvent(monitoredLocationId);

        // Act
        await _weatherAlertEmailNotificationDomainEventHandler.Handle(weatherAlertIssuedDomainEvent,
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            _fakeOutbox.Messages.Should().ContainSingle();
            var (topicName, kafkaKey, message) = _fakeOutbox.Messages[0];
            topicName.Should().Be("notifications.commands");
            kafkaKey.Should().Be(subscriber.UserId.ToString());
            message.Should().BeOfType<SendEmailNotificationCommand>();
        }
    }

    [Fact]
    public async Task Handle_WhenMultipleSubscribers_AddsOutboxMessageForEach()
    {
        // Arrange
        var monitoredLocationId = Guid.CreateVersion7();
        var subscriber1 = await CreateSubscriberWithSubscription(monitoredLocationId);
        var subscriber2 = await CreateSubscriberWithSubscription(monitoredLocationId);
        var subscriber3 = await CreateSubscriberWithSubscription(monitoredLocationId);
        var domainEvent = CreateWeatherAlertIssuedDomainEvent(monitoredLocationId);

        // Act
        await _weatherAlertEmailNotificationDomainEventHandler.Handle(domainEvent, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            _fakeOutbox.Messages.Should().HaveCount(3);
            var kafkaKeys = _fakeOutbox.Messages.Select(m => m.KafkaKey).ToList();
            kafkaKeys.Should().Contain(subscriber1.UserId.ToString());
            kafkaKeys.Should().Contain(subscriber2.UserId.ToString());
            kafkaKeys.Should().Contain(subscriber3.UserId.ToString());
        }
    }

    [Fact]
    public async Task Handle_WhenSubscriberNotSubscribedToLocation_AddsNoOutboxMessage()
    {
        // Arrange
        var subscribedLocationId = Guid.CreateVersion7();
        var alertLocationId = Guid.CreateVersion7(); // Different location
        await CreateSubscriberWithSubscription(subscribedLocationId);
        var weatherAlertIssuedDomainEvent = CreateWeatherAlertIssuedDomainEvent(alertLocationId);

        // Act
        await _weatherAlertEmailNotificationDomainEventHandler.Handle(weatherAlertIssuedDomainEvent,
            CancellationToken.None);

        // Assert
        _fakeOutbox.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SetsCorrectTemplateId()
    {
        // Arrange
        var monitoredLocationId = Guid.CreateVersion7();
        await CreateSubscriberWithSubscription(monitoredLocationId);
        var weatherAlertIssuedDomainEvent = CreateWeatherAlertIssuedDomainEvent(monitoredLocationId);

        // Act
        await _weatherAlertEmailNotificationDomainEventHandler.Handle(weatherAlertIssuedDomainEvent,
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            _fakeOutbox.Messages.Should().ContainSingle();
            var sendEmailNotificationCommand = (SendEmailNotificationCommand)_fakeOutbox.Messages[0].Message;
            sendEmailNotificationCommand.TemplateId.Should().Be("weather-alerts.weather-alert");
        }
    }

    [Fact]
    public async Task Handle_SetsCorrectIdempotencyKey()
    {
        // Arrange
        var monitoredLocationId = Guid.CreateVersion7();
        var subscriber = await CreateSubscriberWithSubscription(monitoredLocationId);
        var weatherAlertIssuedDomainEvent = CreateWeatherAlertIssuedDomainEvent(monitoredLocationId);
        var expectedIdempotencyKey =
            $"weather-alert-{subscriber.UserId}-{monitoredLocationId}-{weatherAlertIssuedDomainEvent.IssuedAtUtc:O}";

        // Act
        await _weatherAlertEmailNotificationDomainEventHandler.Handle(weatherAlertIssuedDomainEvent,
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            _fakeOutbox.Messages.Should().ContainSingle();
            var sendEmailNotificationCommand = (SendEmailNotificationCommand)_fakeOutbox.Messages[0].Message;
            sendEmailNotificationCommand.IdempotencyKey.Should().Be(expectedIdempotencyKey);
        }
    }

    [Fact]
    public async Task Handle_SetsCorrectTemplateData()
    {
        // Arrange
        var monitoredLocationId = Guid.CreateVersion7();
        await CreateSubscriberWithSubscription(monitoredLocationId);
        var weatherAlertIssuedDomainEvent = CreateWeatherAlertIssuedDomainEvent(monitoredLocationId, "Prague",
            CountryCode.CZ,
            AlertType.HighTemperature);

        // Act
        await _weatherAlertEmailNotificationDomainEventHandler.Handle(weatherAlertIssuedDomainEvent,
            CancellationToken.None);

        // Assert
        var sendEmailNotificationCommand = (SendEmailNotificationCommand)_fakeOutbox.Messages[0].Message;
        using (new AssertionScope())
        {
            sendEmailNotificationCommand.TemplateData.Should()
                .ContainKey("city").WhoseValue.Should().Be("Prague");
            sendEmailNotificationCommand.TemplateData.Should()
                .ContainKey("countryCode").WhoseValue.Should().Be("CZ");
            sendEmailNotificationCommand.TemplateData.Should()
                .ContainKey("alertType").WhoseValue.Should().Be("HighTemperature");
            sendEmailNotificationCommand.TemplateData.Should().ContainKey("severity");
            sendEmailNotificationCommand.TemplateData.Should().ContainKey("message");
            sendEmailNotificationCommand.TemplateData.Should().ContainKey("temperature");
            sendEmailNotificationCommand.TemplateData.Should().ContainKey("humidity");
            sendEmailNotificationCommand.TemplateData.Should().ContainKey("windSpeed");
        }
    }

    private async Task<AlertSubscriber> CreateSubscriberWithSubscription(Guid monitoredLocationId)
    {
        var subscriber = AlertSubscriber.CreateFree(Guid.CreateVersion7());
        subscriber.SubscribeToMonitoredLocation(monitoredLocationId);
        subscriber.PopDomainEvents(); // Clear domain events

        _dbContext.AlertSubscribers.Add(subscriber);
        await _dbContext.SaveChangesAsync();

        return subscriber;
    }

    private static WeatherAlertIssuedDomainEvent CreateWeatherAlertIssuedDomainEvent(
        Guid monitoredLocationId,
        string city = "Prague",
        CountryCode? countryCode = null,
        AlertType alertType = AlertType.HighTemperature)
    {
        return new WeatherAlertIssuedDomainEvent
        {
            MonitoredLocationId = monitoredLocationId,
            City = City.Create(city).Value,
            CountryCode = countryCode ?? CountryCode.CZ,
            WeatherAlert = WeatherAlert.Create(alertType, AlertSeverity.Warning, $"{alertType} alert: Test message").Value,
            TriggeringReading = WeatherReading.Create(
                Temperature.FromCelsius(40).Value,
                Humidity.FromPercent(50).Value,
                WindSpeed.FromKilometersPerHour(15).Value,
                UtcNow),
            IssuedAtUtc = UtcNow
        };
    }

    /// <summary>
    /// Fake implementation of ITransactionalOutbox that captures messages for verification.
    /// </summary>
    private sealed class FakeTransactionalOutbox : ITransactionalOutbox<IWeatherDbContext>
    {
        public List<(string TopicName, string? KafkaKey, ISpecificRecord Message)> Messages { get; } = [];

        public void AddOutboxMessage(string topicName, string? kafkaKey, ISpecificRecord integrationEvent)
        {
            Messages.Add((topicName, kafkaKey, integrationEvent));
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public DatabaseFacade Database => null!;
    }
}
