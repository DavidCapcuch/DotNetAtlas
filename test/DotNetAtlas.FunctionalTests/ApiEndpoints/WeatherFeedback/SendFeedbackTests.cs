using System.Net;
using DotNetAtlas.Api.Endpoints.Weather;
using DotNetAtlas.Application.Feedback.SendFeedback;
using DotNetAtlas.FunctionalTests.Common;
using FastEndpoints;

namespace DotNetAtlas.FunctionalTests.ApiEndpoints.WeatherFeedback;

[Collection<FeedbackTestCollection>]
public class SendFeedbackTests : BaseApiTest
{
    public SendFeedbackTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenSendingTooLongFeedback_ReturnsTooLongError()
    {
        // Arrange
        var sendFeedbackCommand = new SendFeedbackCommand
        {
            Feedback = new string('a', 501),
            Rating = 6,
            UserId = Guid.CreateVersion7()
        };

        // Act
        var (httpResponse, problemDetails) =
            await HttpClientRegistry.PlebClient.POSTAsync<SendFeedbackEndpoint, SendFeedbackCommand, ProblemDetails>(
                sendFeedbackCommand);

        // Assert
        using (new AssertionScope())
        {
            httpResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            problemDetails.Errors.Should().HaveCount(2);
            problemDetails.Errors.Should()
                .ContainSingle(err => err.Reason == "Feedback cannot exceed 500 characters.")
                .And.ContainSingle(err => err.Reason == "Rating must be between 1 and 5.");
        }
    }

    [Fact]
    public async Task WhenNotAuthenticated_ReturnsUnauthorized()
    {
        // Arrange
        var sendFeedbackCommand = new SendFeedbackCommand
        {
            Feedback = "",
            Rating = 5,
            UserId = Guid.Empty
        };

        // Act
        var httpResponse =
            await HttpClientRegistry.NonAuthClient.POSTAsync<SendFeedbackEndpoint, SendFeedbackCommand>(
                sendFeedbackCommand);

        // Assert
        httpResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
