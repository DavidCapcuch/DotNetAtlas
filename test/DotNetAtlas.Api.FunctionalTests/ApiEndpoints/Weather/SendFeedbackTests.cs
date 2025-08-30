using System.Net;
using AwesomeAssertions;
using AwesomeAssertions.Execution;
using DotNetAtlas.Api.Endpoints.Weather;
using DotNetAtlas.Api.FunctionalTests.Base;
using DotNetAtlas.Application.Feedback.SendFeedback;
using FastEndpoints;

namespace DotNetAtlas.Api.FunctionalTests.ApiEndpoints.Weather
{
    [Collection<CollectionA>]
    public class SendFeedbackTests : BaseApiTest
    {
        public SendFeedbackTests(ApiTestFixture app, ITestOutputHelper testOutputHelper)
            : base(app, testOutputHelper)
        {
        }

        [Fact]
        public async Task WhenSendingTooLongFeedback_ReturnsTooLongError()
        {
            // Arrange and Act
            var (httpResponse, problemDetails) =
                await PlebClient.POSTAsync<SendFeedbackEndpoint, SendFeedbackCommand, ProblemDetails>(
                    new SendFeedbackCommand
                    {
                        Feedback = new string('a', 501),
                        Rating = 6,
                        UserId = Guid.CreateVersion7()
                    });

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
            // Arrange and Act
            var httpResponse =
                await NonAuthClient.POSTAsync<SendFeedbackEndpoint, SendFeedbackCommand>(
                    new SendFeedbackCommand
                    {
                        Feedback = "",
                        Rating = 5,
                        UserId = Guid.Empty
                    });

            // Assert
            httpResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }
}