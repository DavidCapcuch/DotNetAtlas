using System.Net;
using AwesomeAssertions;
using AwesomeAssertions.Execution;
using DotNetAtlas.Api.Endpoints.Weather;
using DotNetAtlas.Application.Forecast.GetForecasts;
using DotNetAtlas.FunctionalTests.Base;
using FastEndpoints;

namespace DotNetAtlas.FunctionalTests.ApiEndpoints.Weather;

[Collection<CollectionA>]
public class GetForecastsTests : BaseApiTest
{
    public GetForecastsTests(ApiTestFixture app, ITestOutputHelper testOutputHelper)
        : base(app, testOutputHelper)
    {
    }

    [Fact]
    public async Task WhenRequestingValidDays_ReturnsOkWithExpectedCount()
    {
        // Arrange
        const int numberOfDaysForecast = 5;

        // Act
        var (httpResponse, forecastResponse) =
            await NonAuthClient.GETAsync<GetForecastEndpoint, GetForecastQuery, GetForecastResponse>(
                new GetForecastQuery
                {
                    Days = numberOfDaysForecast,
                    City = "Prague",
                    CountryCode = CountryCode.Cz
                });

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
        // Arrange and Act
        var (httpResponse, problemDetails) =
            await NonAuthClient.GETAsync<GetForecastEndpoint, GetForecastQuery, ProblemDetails>(
                new GetForecastQuery
                {
                    Days = 20,
                    City = "Prague",
                    CountryCode = CountryCode.Cz
                });

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
        // Arrange and Act
        var (httpResponse, problemDetails) =
            await NonAuthClient.GETAsync<GetForecastEndpoint, GetForecastQuery, ProblemDetails>(
                new GetForecastQuery
                {
                    Days = 3,
                    City = "TotallyNotACity",
                    CountryCode = CountryCode.Cz
                });

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
        // Arrange and Act
        var (httpResponse, problemDetails) =
            await NonAuthClient.GETAsync<GetForecastEndpoint, GetForecastQuery, ProblemDetails>(
                new GetForecastQuery
                {
                    Days = 3,
                    City = new string('a', 101),
                    CountryCode = CountryCode.Cz
                });

        // Assert
        var error = problemDetails.Errors.First();
        using (new AssertionScope())
        {
            httpResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            problemDetails.Errors.Should().ContainSingle();
            error.Reason.Should().Contain("The length of 'city' must be 100 characters or fewer.");
        }
    }
}
