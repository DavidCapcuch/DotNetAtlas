using System.Diagnostics;
using System.Net;
using DotNetAtlas.Api.Endpoints.Weather;
using DotNetAtlas.Application.Feedback.ChangeFeedback;
using DotNetAtlas.Application.Feedback.SendFeedback;
using DotNetAtlas.FunctionalTests.Common;
using DotNetAtlas.FunctionalTests.Common.Clients;
using FastEndpoints;

namespace DotNetAtlas.FunctionalTests.ApiEndpoints.WeatherFeedback;

[Collection<FeedbackTestCollection>]
public class ChangeFeedbackTests : BaseApiTest
{
    public ChangeFeedbackTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenUpdatingWithInvalidFeedback_ReturnsBadRequest()
    {
        // Arrange
        var changeFeedbackCommand = new ChangeFeedbackCommand
        {
            Id = Guid.CreateVersion7(),
            Feedback = new string('a', 501),
            Rating = 6
        };

        // Act
        var (httpResponse, problemDetails) =
            await HttpClientRegistry.PlebClient.PUTAsync<ChangeFeedbackEndpoint, ChangeFeedbackCommand, ProblemDetails>(
                changeFeedbackCommand);

        // Assert
        using (new AssertionScope())
        {
            httpResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            problemDetails.Errors.Should().HaveCount(2);
        }
    }

    [Fact]
    public async Task WhenNotAuthenticated_ReturnsUnauthorized()
    {
        // Arrange
        var changeFeedbackCommand = new ChangeFeedbackCommand
        {
            Id = Guid.Empty,
            Feedback = "Good",
            Rating = 4
        };

        // Act
        var httpResponse =
            await HttpClientRegistry.NonAuthClient.PUTAsync<ChangeFeedbackEndpoint, ChangeFeedbackCommand>(
                changeFeedbackCommand);

        // Assert
        httpResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WhenFeedbackDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        var changeFeedbackCommand = new ChangeFeedbackCommand
        {
            Id = Guid.NewGuid(),
            Feedback = "Updated feedback",
            Rating = 5
        };

        // Act
        var (httpResponse, problemDetails) =
            await HttpClientRegistry.PlebClient.PUTAsync<ChangeFeedbackEndpoint, ChangeFeedbackCommand, ProblemDetails>(
                changeFeedbackCommand);

        // Assert
        using (new AssertionScope())
        {
            httpResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
            problemDetails.Errors.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task WhenOtherUserTryingToUpdate_ReturnsForbidden()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var createResponse =
            await HttpClientRegistry.PlebClient.POSTAsync<SendFeedbackEndpoint, SendFeedbackCommand>(
                new SendFeedbackCommand
                {
                    Feedback = "Initial feedback",
                    Rating = 3,
                    UserId = userId
                });

        var locationPath = createResponse.Headers.Location!.OriginalString;
        var createdFeedbackId = Guid.Parse(locationPath.Split('/').Last());

        using var otherUser = HttpClientRegistry.CreateHttpClient(ClientType.Pleb, Activity.Current?.Id);

        // Act
        var (httpResponse, problemDetails) =
            await otherUser.PUTAsync<ChangeFeedbackEndpoint, ChangeFeedbackCommand, ProblemDetails>(
                new ChangeFeedbackCommand
                {
                    Id = createdFeedbackId,
                    Feedback = "Hacked feedback",
                    Rating = 1
                });

        // Assert
        using (new AssertionScope())
        {
            httpResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            problemDetails.Errors.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task WhenUpdatingOwnFeedback_UpdatesSuccessfully()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        const string initialFeedback = "Initial feedback";
        const string updatedFeedbackText = "Updated feedback text";

        var createResponse =
            await HttpClientRegistry.PlebClient.POSTAsync<SendFeedbackEndpoint, SendFeedbackCommand>(
                new SendFeedbackCommand
                {
                    Feedback = initialFeedback,
                    Rating = 3,
                    UserId = userId
                });

        var locationPath = createResponse.Headers.Location!.OriginalString;
        var feedbackId = Guid.Parse(locationPath.Split('/').Last());

        // Act
        var httpResponse =
            await HttpClientRegistry.PlebClient.PUTAsync<ChangeFeedbackEndpoint, ChangeFeedbackCommand>(
                new ChangeFeedbackCommand
                {
                    Id = feedbackId,
                    Feedback = updatedFeedbackText,
                    Rating = 5
                });

        // Assert
        using (new AssertionScope())
        {
            httpResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
            var updatedFeedback =
                await DbContext.WeatherFeedbacks.FindAsync([feedbackId], TestContext.Current.CancellationToken);
            updatedFeedback!.Feedback.Value.Should().Be(updatedFeedbackText);
            updatedFeedback.Rating.Value.Should().Be(5);
        }
    }
}
