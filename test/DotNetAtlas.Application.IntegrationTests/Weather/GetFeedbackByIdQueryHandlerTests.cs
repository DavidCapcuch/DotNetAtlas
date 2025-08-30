using AwesomeAssertions;
using AwesomeAssertions.Execution;
using DotNetAtlas.Application.Feedback.GetFeedback;
using DotNetAtlas.Application.IntegrationTests.Base;
using DotNetAtlas.Domain.Errors.Base;
using DotNetAtlas.Infrastructure.Persistence.Database.Seed;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.Application.IntegrationTests.Weather
{
    [Collection<CollectionA>]
    public class GetFeedbackByIdQueryHandlerTests : BaseIntegrationTest
    {
        private readonly GetFeedbackByIdQueryHandler _getFeedbackByIdQueryHandler;

        public GetFeedbackByIdQueryHandlerTests(IntegrationTestFixture app, ITestOutputHelper testOutputHelper)
            : base(app, testOutputHelper)
        {
            _getFeedbackByIdQueryHandler =
                new GetFeedbackByIdQueryHandler(
                    Scope.ServiceProvider.GetRequiredService<ILogger<GetFeedbackByIdQueryHandler>>(), DbContext);
        }

        [Fact]
        public async Task WhenExistingId_ReturnsFeedbackResponse()
        {
            // Arrange
            var seedFeedback = new WeatherFeedbackFaker().Generate();
            DbContext.Add(seedFeedback);
            await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

            // Act
            var result = await _getFeedbackByIdQueryHandler.HandleAsync(
                new GetFeedbackByIdQuery
                {
                    Id = seedFeedback.Id
                },
                TestContext.Current.CancellationToken);

            // Assert
            using (new AssertionScope())
            {
                result.Should().BeSuccess();
                var response = result.Value;
                response.Id.Should().Be(seedFeedback.Id);
                response.CreatedByUser.Should().Be(seedFeedback.CreatedByUser);
            }
        }

        [Fact]
        public async Task WhenUnknownId_ReturnsNotFoundError()
        {
            // Arrange
            var unknownId = Guid.CreateVersion7();

            // Act
            var result = await _getFeedbackByIdQueryHandler.HandleAsync(
                new GetFeedbackByIdQuery
                {
                    Id = unknownId
                },
                TestContext.Current.CancellationToken);

            // Assert
            using (new AssertionScope())
            {
                result.Should().BeFailure();
                result.Errors.Should().HaveCount(1);
                var error = result.Errors.First();
                error.Should().BeAssignableTo<NotFoundError>();
            }
        }
    }
}