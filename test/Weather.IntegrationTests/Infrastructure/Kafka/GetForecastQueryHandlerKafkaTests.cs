using Confluent.Kafka;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Platform.CQRS;
using Weather.Application.WeatherForecast.Common;
using Weather.Application.WeatherForecast.GetForecasts;
using Weather.Application.WeatherForecast.Services.Abstractions;
using Weather.Domain.Forecast.ValueObjects;
using Weather.Forecast;
using Weather.IntegrationTests.Common;
using DomainCountryCode = Weather.Domain.Common.ValueObjects.CountryCode;

namespace Weather.IntegrationTests.Infrastructure.Kafka;

[Collection<IntegrationTestCollection>]
public class GetForecastQueryHandlerKafkaTests : BaseIntegrationTest
{
    private readonly IQueryHandler<GetForecastQuery, GetForecastResponse> _getForecastQueryHandler;

    public GetForecastQueryHandlerKafkaTests(IntegrationTestFixture app)
        : base(app)
    {
        _getForecastQueryHandler =
            Scope.ServiceProvider.GetRequiredService<IQueryHandler<GetForecastQuery, GetForecastResponse>>();
    }

    [Fact]
    public async Task WhenHandlerInvoked_PublishedEventContainsCorrectData()
    {
        // Arrange
        var getForecastQuery = new GetForecastQuery
        {
            City = "Paris",
            CountryCode = DomainCountryCode.FR,
            Days = 4,
            UserId = Guid.CreateVersion7()
        };

        var consumer = KafkaTestConsumerRegistry.ForecastRequestedConsumer;

        // Act
        var result =
            await _getForecastQueryHandler.HandleAsync(getForecastQuery, TestContext.Current.CancellationToken);

        var forecastRequestedEvent =
            consumer.ConsumeOne(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            forecastRequestedEvent.Should().NotBeNull();
            forecastRequestedEvent.City.Should().Be(getForecastQuery.City);
            forecastRequestedEvent.CountryCode.ToString().Should().Be(getForecastQuery.CountryCode.ToString());
            forecastRequestedEvent.Days.Should().Be(getForecastQuery.Days);
            forecastRequestedEvent.UserId.Should().Be(getForecastQuery.UserId);
            forecastRequestedEvent.OccurredOnUtc.Should().BeOnOrAfter(DateTime.UtcNow.AddSeconds(-5));
        }
    }

    [Theory]
    [InlineData("New York", DomainCountryCode.US, CountryCode.US)]
    [InlineData("London", DomainCountryCode.GB, CountryCode.GB)]
    [InlineData("Berlin", DomainCountryCode.DE, CountryCode.DE)]
    [InlineData("Paris", DomainCountryCode.FR, CountryCode.FR)]
    [InlineData("Prague", DomainCountryCode.CZ, CountryCode.CZ)]
    public async Task WhenDifferentCountryCodes_PublishesEventWithCorrectCountryCode(
        string city,
        DomainCountryCode domainCountryCode,
        CountryCode expectedKafkaCountryCode)
    {
        // Arrange

        var getForecastQuery = new GetForecastQuery
        {
            City = city,
            CountryCode = domainCountryCode,
            Days = 1,
            UserId = Guid.CreateVersion7()
        };

        var consumer = KafkaTestConsumerRegistry.ForecastRequestedConsumer;

        // Act
        var result =
            await _getForecastQueryHandler.HandleAsync(getForecastQuery, TestContext.Current.CancellationToken);

        var forecastRequestedEvent =
            consumer.ConsumeOne(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            forecastRequestedEvent.Should().NotBeNull();
            forecastRequestedEvent.City.Should().Be(city);
            forecastRequestedEvent.CountryCode.Should().Be(expectedKafkaCountryCode);
            forecastRequestedEvent.OccurredOnUtc.Should().BeOnOrAfter(DateTime.UtcNow.AddSeconds(-5));
        }
    }

    [Fact]
    public async Task WhenHandlerInvokedConcurrently_PublishesAllEventsSuccessfully()
    {
        // Arrange
        var consumer = KafkaTestConsumerRegistry.ForecastRequestedConsumer;

        var cities = new[]
        {
            "Prague", "London", "Berlin", "Paris", "Madrid"
        };
        var getForecastQueries = cities.Select((city, i) => new GetForecastQuery
        {
            City = city,
            CountryCode = i switch
            {
                0 => DomainCountryCode.CZ,
                1 => DomainCountryCode.GB,
                2 => DomainCountryCode.DE,
                3 => DomainCountryCode.FR,
                4 => DomainCountryCode.ES,
                _ => DomainCountryCode.US
            },
            Days = i + 1,
            UserId = Guid.CreateVersion7()
        }).ToList();
        var expectedCount = getForecastQueries.Count;

        // Act
        var getForecastTasks = getForecastQueries
            .Select(query => _getForecastQueryHandler.HandleAsync(query, TestContext.Current.CancellationToken));

        var results = await Task.WhenAll(getForecastTasks);

        var events = consumer
            .ConsumeMultiple(TimeSpan.FromSeconds(3), expectedCount, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            results.Should().AllSatisfy(r => r.Should().BeSuccess());
            events.Should().HaveCount(expectedCount);

            foreach (var getForecastQuery in getForecastQueries)
            {
                events.Should().Contain(e =>
                    e.City == getForecastQuery.City &&
                    e.CountryCode.ToString() == getForecastQuery.CountryCode.ToString() &&
                    e.Days == getForecastQuery.Days &&
                    e.UserId == getForecastQuery.UserId);
            }
        }
    }

    [Fact]
    public async Task WhenKafkaProducerFails_HandlerStillSucceeds()
    {
        // Arrange
        // Create a failing producer that throws when publishing
        var failingProducer = Substitute.For<IForecastEventsProducer>();
        failingProducer
            .When(x => x.PublishForecastRequestedFireAndForgetAsync(Arg.Any<ForecastCriteria>(), Arg.Any<Guid?>()))
            .Do(_ => throw new KafkaException(ErrorCode.BrokerNotAvailable));

        // Get required services
        var weatherForecastService = Scope.ServiceProvider.GetRequiredService<IWeatherForecastService>();
        var timeProvider = Scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var queryHandlerLogger = Scope.ServiceProvider.GetRequiredService<ILogger<GetForecastQueryHandler>>();

        // Create the query handler with the failing producer
        var getForecastQueryHandlerWithFailingProducer = new GetForecastQueryHandler(
            weatherForecastService,
            failingProducer,
            queryHandlerLogger,
            timeProvider);

        var getForecastQuery = new GetForecastQuery
        {
            City = "Prague",
            CountryCode = DomainCountryCode.CZ,
            Days = 1,
            UserId = Guid.CreateVersion7()
        };

        // Act
        var result =
            await getForecastQueryHandlerWithFailingProducer.HandleAsync(getForecastQuery,
                TestContext.Current.CancellationToken);

        // Assert - handler should succeed even though Kafka publishing fails
        // because the handler uses fire-and-forget pattern
        using (new AssertionScope())
        {
            result.Should().BeSuccess("handler uses fire-and-forget pattern for Kafka publishing");
            result.Value.Should().NotBeNull();
            result.Value.Forecasts.Should().ContainSingle();
        }
    }
}
