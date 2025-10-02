using System.Net;
using DotNetAtlas.Api.Endpoints.Weather;
using DotNetAtlas.Application.Feedback.GetFeedback;
using DotNetAtlas.FunctionalTests.Common;
using DotNetAtlas.Infrastructure.Persistence.Database.Seed;
using FastEndpoints;

namespace DotNetAtlas.FunctionalTests.ApiEndpoints.Feedback;

[Collection<FeedbackTestCollection>]
public class GetFeedbackByIdTests : BaseApiTest
{
    public GetFeedbackByIdTests(ApiTestFixture app, ITestOutputHelper testOutputHelper)
        : base(app, testOutputHelper)
    {
    }

    [Fact]
    public async Task WhenRequestingEmptyGuid_ReturnsBadRequestMustNotBeEmpty()
    {
        // Arrange and Act
        var (httpResponse, problemDetails) =
            await PlebClient.GETAsync<GetFeedbackByIdEndpoint, GetFeedbackByIdQuery, ProblemDetails>(
                new GetFeedbackByIdQuery
                {
                    Id = Guid.Empty
                });

        // Assert
        var error = problemDetails.Errors.First();
        using (new AssertionScope())
        {
            httpResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            problemDetails.Errors.Should().ContainSingle();
            error.Reason.Should().Be("Feedback ID must not be empty.");
        }
    }

    [Fact]
    public async Task WhenNotAuthenticated_ReturnsUnauthorized()
    {
        // Arrange and Act
        var httpResponse =
            await NonAuthClient.GETAsync<GetFeedbackByIdEndpoint, GetFeedbackByIdQuery>(
                new GetFeedbackByIdQuery
                {
                    Id = Guid.Empty
                });

        // Assert
        httpResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WhenRequestingSeedFeedback_ReturnsSeedFeedback()
    {
        // Arrange
        var seedFeedback = new WeatherFeedbackFaker().Generate();
        DbContext.Add(seedFeedback);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var (httpResponse, feedback) =
            await PlebClient.GETAsync<GetFeedbackByIdEndpoint, GetFeedbackByIdQuery, GetFeedbackByIdResponse>(
                new GetFeedbackByIdQuery
                {
                    Id = seedFeedback.Id
                });

        // Assert
        using (new AssertionScope())
        {
            httpResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            feedback.Id.Should().Be(seedFeedback.Id);
        }
    }
}
