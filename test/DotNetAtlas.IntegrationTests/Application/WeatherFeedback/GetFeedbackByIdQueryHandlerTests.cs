using DotNetAtlas.Application.WeatherFeedback.GetFeedback;
using DotNetAtlas.CQS;
using DotNetAtlas.Infrastructure.Persistence.Database.Seed;
using DotNetAtlas.IntegrationTests.Common;
using DotNetAtlas.SharedKernel.Errors;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetAtlas.IntegrationTests.Application.WeatherFeedback;

[Collection<ForecastTestCollection>]
public class GetFeedbackByIdQueryHandlerTests : BaseIntegrationTest
{
    private readonly IQueryHandler<GetFeedbackByIdQuery, GetFeedbackByIdResponse> _getFeedbackByIdQueryHandler;

    public GetFeedbackByIdQueryHandlerTests(IntegrationTestFixture app)
        : base(app)
    {
        _getFeedbackByIdQueryHandler =
            Scope.ServiceProvider.GetRequiredService<IQueryHandler<GetFeedbackByIdQuery, GetFeedbackByIdResponse>>();
    }

    [Fact]
    public async Task WhenExistingId_ReturnsFeedbackResponse()
    {
        // Arrange
        var seedFeedback = new WeatherFeedbackFaker().Generate();
        WeatherDbContext.Add(seedFeedback);
        await WeatherDbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

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
            result.Errors.Should().ContainSingle();
            var error = result.Errors[0];
            error.Should().BeAssignableTo<NotFoundError>();
        }
    }
}
