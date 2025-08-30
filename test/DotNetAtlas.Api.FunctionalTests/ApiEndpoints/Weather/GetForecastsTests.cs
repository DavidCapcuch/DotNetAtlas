using System.Net;
using AwesomeAssertions;
using AwesomeAssertions.Execution;
using DotNetAtlas.Api.Endpoints.Weather;
using DotNetAtlas.Api.FunctionalTests.Base;
using DotNetAtlas.Application.Forecast.GetForecasts;
using FastEndpoints;

namespace DotNetAtlas.Api.FunctionalTests.ApiEndpoints.Weather
{
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
            var (httpResponse, forecastsResponses) =
                await NonAuthClient.GETAsync<GetForecastsEndpoint, GetForecastsQuery, GetForecastsResponse>(
                    new GetForecastsQuery
                    {
                        Days = numberOfDaysForecast
                    });

            var forecasts =
                await forecastsResponses.Forecasts.ToListAsync(TestContext.Current.CancellationToken);

            // Assert
            using (new AssertionScope())
            {
                httpResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                forecasts.Count.Should().Be(numberOfDaysForecast);
            }
        }

        [Fact]
        public async Task WhenRequestingTooManyDays_ReturnsBadRequest()
        {
            // Arrange and Act
            var (httpResponse, problemDetails) =
                await NonAuthClient.GETAsync<GetForecastsEndpoint, GetForecastsQuery, ProblemDetails>(
                    new GetForecastsQuery
                    {
                        Days = 20
                    });

            // Assert
            var error = problemDetails.Errors.First();
            using (new AssertionScope())
            {
                httpResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
                problemDetails.Errors.Should().HaveCount(1);
                error.Reason.Should().Be("Days must be between 1 and 14.");
            }
        }
    }
}