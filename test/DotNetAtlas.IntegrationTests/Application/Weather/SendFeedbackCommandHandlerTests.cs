using AwesomeAssertions;
using AwesomeAssertions.Execution;
using DotNetAtlas.Application.Feedback.SendFeedback;
using DotNetAtlas.Domain.Errors.Base;
using DotNetAtlas.IntegrationTests.Base;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.IntegrationTests.Application.Weather;

[Collection<CollectionA>]
public class SendFeedbackCommandHandlerTests : BaseIntegrationTest
{
    private readonly SendFeedbackCommandHandler _sendFeedbackCommandHandler;

    public SendFeedbackCommandHandlerTests(IntegrationTestFixture app, ITestOutputHelper testOutputHelper)
        : base(app, testOutputHelper)
    {
        _sendFeedbackCommandHandler =
            new SendFeedbackCommandHandler(
                Scope.ServiceProvider.GetRequiredService<ILogger<SendFeedbackCommandHandler>>(),
                DbContext);
    }

    [Fact]
    public async Task WhenValidRequest_PersistsFeedbackAndReturnsId()
    {
        // Arrange
        var sendFeedbackCommand = new SendFeedbackCommand
        {
            Feedback = "Great forecast!",
            Rating = 5,
            UserId = Guid.CreateVersion7()
        };

        // Act
        var result =
            await _sendFeedbackCommandHandler.HandleAsync(
                sendFeedbackCommand,
                TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            var createdId = result.Value;
            var exists = await DbContext.WeatherFeedbacks
                .AsNoTracking()
                .AnyAsync(wf => wf.Id == createdId, TestContext.Current.CancellationToken);
            exists.Should().BeTrue();
        }
    }

    [Fact]
    public async Task WhenRatingOutOfRange_ReturnsValidationError()
    {
        // Arrange
        var sendFeedbackCommand = new SendFeedbackCommand
        {
            Feedback = "ok",
            Rating = 0,
            UserId = Guid.CreateVersion7()
        };

        // Act
        var result =
            await _sendFeedbackCommandHandler.HandleAsync(
                sendFeedbackCommand,
                TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.IsFailed.Should().BeTrue();
            result.Errors.Should().NotBeEmpty();
            var validationError = result.Errors[0] as ValidationError;
            validationError.Should().NotBeNull();
            validationError!.ErrorCode.Should().Be("FeedbackRating.OutOfRange");
        }
    }

    [Fact]
    public async Task WhenFeedbackEmpty_ReturnsValidationError()
    {
        // Arrange
        var sendFeedbackCommand = new SendFeedbackCommand
        {
            Feedback = "   ",
            Rating = 3,
            UserId = Guid.CreateVersion7()
        };

        // Act
        var result =
            await _sendFeedbackCommandHandler.HandleAsync(
                sendFeedbackCommand,
                TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle();
            var validationError = result.Errors[0] as ValidationError;
            validationError.Should().NotBeNull();
            validationError!.ErrorCode.Should().Be("WeatherFeedback.FeedbackRequired");
        }
    }

    [Fact]
    public async Task WhenAllPropertiesInvavlid_ReturnsValidationErrorForAll()
    {
        // Arrange
        var sendFeedbackCommand = new SendFeedbackCommand
        {
            Feedback = new string('a', 501),
            Rating = 50,
            UserId = Guid.CreateVersion7()
        };

        // Act
        var result =
            await _sendFeedbackCommandHandler.HandleAsync(
                sendFeedbackCommand,
                TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().HaveCount(2);
            result.Errors.Should().AllBeAssignableTo<ValidationError>();
            var errors = result.Errors.OfType<ValidationError>().ToList();
            errors.Should().HaveCount(2);
            errors.Should()
                .ContainSingle(err => err.ErrorCode == "WeatherFeedback.FeedbackTooLong")
                .And.ContainSingle(err => err.ErrorCode == "FeedbackRating.OutOfRange");
        }
    }
}
