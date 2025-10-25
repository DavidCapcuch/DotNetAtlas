using Confluent.Kafka;
using DotNetAtlas.Application.Common.CQS;
using DotNetAtlas.Application.Forecast.Common.Abstractions;
using DotNetAtlas.Application.Forecast.GetForecasts;
using DotNetAtlas.Application.Forecast.Services.Abstractions;
using DotNetAtlas.Infrastructure.Communication.Kafka.Config;
using DotNetAtlas.IntegrationTests.Common;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Weather.Contracts;
using DomainCountryCode = DotNetAtlas.Domain.Entities.Weather.Forecast.CountryCode;

namespace DotNetAtlas.IntegrationTests.Infrastructure.Kafka;

[Collection<ForecastTestCollection>]
public class GetForecastQueryHandlerKafkaTests : BaseIntegrationTest
{
    private readonly TopicsOptions _topicsOptions;

    public GetForecastQueryHandlerKafkaTests(IntegrationTestFixture app)
        : base(app)
    {
        _topicsOptions = Scope.ServiceProvider.GetRequiredService<IOptions<TopicsOptions>>().Value;
    }

    [Fact]
    public async Task WhenHandlerInvoked_PublishedEventContainsCorrectData()
    {
        // Arrange
        var getForecastQueryHandler =
            Scope.ServiceProvider.GetRequiredService<IQueryHandler<GetForecastQuery, GetForecastResponse>>();
        var getForecastQuery = new GetForecastQuery
        {
            City = "Paris",
            CountryCode = DomainCountryCode.FR,
            Days = 4,
            UserId = Guid.NewGuid()
        };

        var consumer = KafkaTestConsumerRegistry.ForecastRequestedConsumer;

        // Act
        var result =
            await getForecastQueryHandler.HandleAsync(getForecastQuery, TestContext.Current.CancellationToken);

        var forecastRequestedEvent =
            consumer.ConsumeOne(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            forecastRequestedEvent.Should().NotBeNull();
            forecastRequestedEvent!.City.Should().Be(getForecastQuery.City);
            forecastRequestedEvent.CountryCode.ToString().Should().Be(getForecastQuery.CountryCode.ToString());
            forecastRequestedEvent.Days.Should().Be(getForecastQuery.Days);
            forecastRequestedEvent.UserId.Should().Be(getForecastQuery.UserId);
            forecastRequestedEvent.RequestedAtUtc.Should().BeOnOrAfter(DateTime.UtcNow.AddSeconds(-5));
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
        var getForecastQueryHandler =
            Scope.ServiceProvider.GetRequiredService<IQueryHandler<GetForecastQuery, GetForecastResponse>>();

        var getForecastQuery = new GetForecastQuery
        {
            City = city,
            CountryCode = domainCountryCode,
            Days = 1,
            UserId = Guid.NewGuid()
        };

        var consumer = KafkaTestConsumerRegistry.ForecastRequestedConsumer;

        // Act
        var result = await getForecastQueryHandler.HandleAsync(getForecastQuery, TestContext.Current.CancellationToken);

        var forecastRequestedEvent =
            consumer.ConsumeOne(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            forecastRequestedEvent.Should().NotBeNull();
            forecastRequestedEvent!.City.Should().Be(city);
            forecastRequestedEvent.CountryCode.Should().Be(expectedKafkaCountryCode);
            forecastRequestedEvent.RequestedAtUtc.Should().BeOnOrAfter(DateTime.UtcNow.AddSeconds(-5));
        }
    }

    [Fact]
    public async Task WhenHandlerInvokedConcurrently_PublishesAllEventsSuccessfully()
    {
        // Arrange
        var getForecastQueryHandler =
            Scope.ServiceProvider.GetRequiredService<IQueryHandler<GetForecastQuery, GetForecastResponse>>();
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
            UserId = Guid.NewGuid()
        }).ToList();

        // Act
        var getForecastTasks = getForecastQueries
            .Select(query => getForecastQueryHandler.HandleAsync(query, TestContext.Current.CancellationToken));

        var results = await Task.WhenAll(getForecastTasks);

        var events = consumer.ConsumeAll(TimeSpan.FromSeconds(10), 5, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            results.Should().AllSatisfy(r => r.Should().BeSuccess());
            events.Should().HaveCount(5);

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
        var failingProducer = Substitute.For<IForecastEventsProducer>();
        failingProducer.PublishForecastRequestedAsync(
                Arg.Any<GetForecastQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new KafkaException(ErrorCode.BrokerNotAvailable)));

        var forecastService = Scope.ServiceProvider.GetRequiredService<IWeatherForecastService>();
        var logger = Scope.ServiceProvider.GetRequiredService<ILogger<GetForecastQueryHandler>>();

        var getForecastQueryHandler = new GetForecastQueryHandler(
            forecastService,
            logger,
            failingProducer);

        var getForecastQuery = new GetForecastQuery
        {
            City = "Prague",
            CountryCode = DomainCountryCode.CZ,
            Days = 1,
            UserId = Guid.NewGuid()
        };

        // Act
        var result = await getForecastQueryHandler.HandleAsync(getForecastQuery, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess("handler uses fire-and-forget pattern for Kafka publishing");
            result.Value.Should().NotBeNull();
            result.Value.Forecasts.Should().ContainSingle();
        }
    }
}
