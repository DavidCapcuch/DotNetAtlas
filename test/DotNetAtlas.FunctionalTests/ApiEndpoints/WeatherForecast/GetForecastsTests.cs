using System.Net;
using DotNetAtlas.Api.Endpoints.Weather;
using DotNetAtlas.Application.Forecast.GetForecasts;
using DotNetAtlas.Domain.Entities.Weather.Forecast;
using DotNetAtlas.FunctionalTests.Common;
using FastEndpoints;

namespace DotNetAtlas.FunctionalTests.ApiEndpoints.WeatherForecast;

[Collection<ForecastTestCollection>]
public class GetForecastsTests : BaseApiTest
{
    public GetForecastsTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenRequestingValidDays_ReturnsOkWithExpectedCount()
    {
        // Arrange
        const int numberOfDaysForecast = 5;
        var getForecastQuery = new GetForecastQuery
        {
            Days = numberOfDaysForecast,
            City = "Prague",
            CountryCode = CountryCode.CZ
        };

        // Act
        var (httpResponse, forecastResponse) =
            await HttpClientRegistry.NonAuthClient.GETAsync<GetForecastEndpoint, GetForecastQuery, GetForecastResponse>(
                getForecastQuery);

        // Assert
        using (new AssertionScope())
        {
            httpResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            forecastResponse.Forecasts.Should().HaveCount(numberOfDaysForecast);
        }
    }

    [Fact]
    public async Task WhenRequestingTooManyDays_ReturnsBadRequest()
    {
        // Arrange
        var getForecastQuery = new GetForecastQuery
        {
            Days = 20,
            City = "Prague",
            CountryCode = CountryCode.CZ
        };

        // Act
        var (httpResponse, problemDetails) =
            await HttpClientRegistry.NonAuthClient.GETAsync<GetForecastEndpoint, GetForecastQuery, ProblemDetails>(
                getForecastQuery);

        // Assert
        var error = problemDetails.Errors.First();
        using (new AssertionScope())
        {
            httpResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            problemDetails.Errors.Should().ContainSingle();
            error.Reason.Should().Be("Days must be between 1 and 14.");
        }
    }

    [Fact]
    public async Task WhenRequestingUnknownCity_ReturnsNotFound()
    {
        // Arrange
        var getForecastQuery = new GetForecastQuery
        {
            Days = 3,
            City = "TotallyNotACity",
            CountryCode = CountryCode.CZ
        };

        // Act
        var (httpResponse, problemDetails) =
            await HttpClientRegistry.NonAuthClient.GETAsync<GetForecastEndpoint, GetForecastQuery, ProblemDetails>(
                getForecastQuery);

        // Assert
        using (new AssertionScope())
        {
            httpResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
            problemDetails.Errors.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task WhenRequestingTooLongCity_ReturnsBadRequest()
    {
        // Arrange
        var getForecastQuery = new GetForecastQuery
        {
            Days = 3,
            City = new string('a', 101),
            CountryCode = CountryCode.CZ
        };

        // Act
        var (httpResponse, problemDetails) =
            await HttpClientRegistry.NonAuthClient.GETAsync<GetForecastEndpoint, GetForecastQuery, ProblemDetails>(
                getForecastQuery);

        // Assert
        var error = problemDetails.Errors.First();
        using (new AssertionScope())
        {
            httpResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            problemDetails.Errors.Should().ContainSingle();
            error.Reason.Should().Contain("must be 100 characters or fewer.");
        }
    }
}
