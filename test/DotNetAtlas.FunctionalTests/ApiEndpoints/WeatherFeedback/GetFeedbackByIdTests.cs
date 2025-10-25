using System.Net;
using DotNetAtlas.Api.Endpoints.Weather;
using DotNetAtlas.Application.WeatherFeedback.GetFeedback;
using DotNetAtlas.Application.WeatherFeedback.SendFeedback;
using DotNetAtlas.FunctionalTests.Common;
using FastEndpoints;

namespace DotNetAtlas.FunctionalTests.ApiEndpoints.WeatherFeedback;

[Collection<FeedbackTestCollection>]
public class GetFeedbackByIdTests : BaseApiTest
{
    public GetFeedbackByIdTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenFeedbackDoesNotExist_ReturnsNotFound()
    {
        // Arrange and Act
        var (httpResponse, problemDetails) =
            await HttpClientRegistry.PlebClient.GETAsync<GetFeedbackByIdEndpoint, GetFeedbackByIdQuery, ProblemDetails>(
                new GetFeedbackByIdQuery
                {
                    Id = Guid.NewGuid()
                });

        // Assert
        using (new AssertionScope())
        {
            httpResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
            problemDetails.Errors.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task WhenNotAuthenticated_ReturnsUnauthorized()
    {
        // Arrange
        var getFeedbackByIdQuery = new GetFeedbackByIdQuery
        {
            Id = Guid.Empty
        };

        // Act
        var httpResponse =
            await HttpClientRegistry.NonAuthClient.GETAsync<GetFeedbackByIdEndpoint, GetFeedbackByIdQuery>(
                getFeedbackByIdQuery);

        // Assert
        httpResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WhenFeedbackExists_ReturnsFeedback()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        const string feedbackText = "Very accurate forecast!";

        var createResponse =
            await HttpClientRegistry.PlebClient.POSTAsync<SendFeedbackEndpoint, SendFeedbackCommand>(
                new SendFeedbackCommand
                {
                    Feedback = feedbackText,
                    Rating = 4,
                    UserId = userId
                });

        var locationPath = createResponse.Headers.Location!.OriginalString;
        var feedbackId = Guid.Parse(locationPath.Split('/').Last());

        // Act
        var getFeedbackByIdQuery = new GetFeedbackByIdQuery
        {
            Id = feedbackId
        };
        var (httpResponse, feedback) =
            await HttpClientRegistry.PlebClient
                .GETAsync<GetFeedbackByIdEndpoint, GetFeedbackByIdQuery, GetFeedbackByIdResponse>(
                    getFeedbackByIdQuery);

        // Assert
        using (new AssertionScope())
        {
            httpResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            feedback.Feedback.Should().Be(feedbackText);
            feedback.Rating.Should().Be(4);
        }
    }
}
