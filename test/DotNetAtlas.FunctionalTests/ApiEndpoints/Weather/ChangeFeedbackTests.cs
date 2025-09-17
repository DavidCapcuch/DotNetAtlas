using System.Net;
using AwesomeAssertions;
using AwesomeAssertions.Execution;
using DotNetAtlas.Api.Endpoints.Weather;
using DotNetAtlas.Application.Feedback.ChangeFeedback;
using DotNetAtlas.Application.Feedback.SendFeedback;
using DotNetAtlas.FunctionalTests.Base;
using DotNetAtlas.Infrastructure.Persistence.Database.Seed;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace DotNetAtlas.FunctionalTests.ApiEndpoints.Weather;

[Collection<CollectionA>]
public class ChangeFeedbackTests : BaseApiTest
{
    public ChangeFeedbackTests(ApiTestFixture app, ITestOutputHelper testOutputHelper)
        : base(app, testOutputHelper)
    {
    }

    [Fact]
    public async Task WhenSendingInvalidPayload_ReturnsBadRequest()
    {
        // Arrange and Act
        var (httpResponse, problemDetails) =
            await PlebClient.PUTAsync<ChangeFeedbackEndpoint, ChangeFeedbackCommand, ProblemDetails>(
                new ChangeFeedbackCommand
                {
                    Id = Guid.Empty,
                    Feedback = string.Empty,
                    Rating = 0
                });

        // Assert
        using (new AssertionScope())
        {
            httpResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            problemDetails.Errors.Should()
                .Contain(err => err.Reason == "'id' must not be empty.")
                .And.Contain(err => err.Reason == "Feedback cannot be empty.")
                .And.Contain(err => err.Reason == "Rating must be between 1 and 5.");
        }
    }

    [Fact]
    public async Task WhenNotAuthenticated_ReturnsUnauthorized()
    {
        // Arrange and Act
        var httpResponse =
            await NonAuthClient.PUTAsync<ChangeFeedbackEndpoint, ChangeFeedbackCommand>(
                new ChangeFeedbackCommand
                {
                    Id = Guid.CreateVersion7(),
                    Feedback = "x",
                    Rating = 1
                });

        // Assert
        httpResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WhenFeedbackDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        var nonExistingId = Guid.CreateVersion7();

        // Act
        var (httpResponse, problemDetails) =
            await PlebClient.PUTAsync<ChangeFeedbackEndpoint, ChangeFeedbackCommand, ProblemDetails>(
                new ChangeFeedbackCommand
                {
                    Id = nonExistingId,
                    Feedback = "Updated feedback",
                    Rating = 4
                });

        // Assert
        using (new AssertionScope())
        {
            httpResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
            problemDetails.Errors.Should().ContainSingle(err => err.Reason.Contains("not found"));
        }
    }

    [Fact]
    public async Task WhenChangingOthersFeedback_ReturnsForbidden()
    {
        // Arrange: seed feedback with a different user than pleb token user
        var seed = new WeatherFeedbackFaker().Generate();
        DbContext.Add(seed);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var (httpResponse, problemDetails) =
            await PlebClient.PUTAsync<ChangeFeedbackEndpoint, ChangeFeedbackCommand, ProblemDetails>(
                new ChangeFeedbackCommand
                {
                    Id = seed.Id,
                    Feedback = "Hacked feedback",
                    Rating = 2
                });

        // Assert
        using (new AssertionScope())
        {
            httpResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            problemDetails.Errors.Should().ContainSingle(err => err.Name.Contains("Forbidden"));
        }
    }

    [Fact]
    public async Task WhenValidRequestFromOwner_UpdatesAndReturnsOk()
    {
        // Arrange: create feedback owned by the current pleb user
        // We can't extract the token's name-identifier easily here, so create first via POST then change.
        var createResponse =
            await PlebClient.POSTAsync<SendFeedbackEndpoint, SendFeedbackCommand>(
                new SendFeedbackCommand
                {
                    Feedback = "Original",
                    Rating = 5
                });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created =
            await DbContext.WeatherFeedbacks
                .OrderByDescending(wf => wf.Id)
                .FirstAsync(TestContext.Current.CancellationToken);

        // Act
        var httpResponse =
            await PlebClient.PUTAsync<ChangeFeedbackEndpoint, ChangeFeedbackCommand>(
                new ChangeFeedbackCommand
                {
                    Id = created.Id,
                    Feedback = "Updated text",
                    Rating = 3
                });

        // Assert
        await DbContext.Entry(created).ReloadAsync(TestContext.Current.CancellationToken);
        using (new AssertionScope())
        {
            httpResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
            created.Feedback.Value.Should().Be("Updated text");
            created.Rating.Value.Should().Be(3);
        }
    }
}
