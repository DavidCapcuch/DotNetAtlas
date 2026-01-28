using DotNetAtlas.Application.WeatherFeedback.SendFeedback;
using DotNetAtlas.CQS;
using DotNetAtlas.IntegrationTests.Common;
using DotNetAtlas.SharedKernel.Errors;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetAtlas.IntegrationTests.Application.WeatherFeedback;

[Collection<ForecastTestCollection>]
public class SendFeedbackCommandHandlerTests : BaseIntegrationTest
{
    private readonly ICommandHandler<SendFeedbackCommand, Guid> _sendFeedbackCommandHandler;

    public SendFeedbackCommandHandlerTests(IntegrationTestFixture app)
        : base(app)
    {
        _sendFeedbackCommandHandler =
            Scope.ServiceProvider.GetRequiredService<ICommandHandler<SendFeedbackCommand, Guid>>();
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
        var sendFeedbackResult =
            await _sendFeedbackCommandHandler.HandleAsync(
                sendFeedbackCommand,
                TestContext.Current.CancellationToken);

        // Assert
        var createdId = sendFeedbackResult.Value;
        var feedbackExists = await WeatherDbContext.Feedbacks
            .AsNoTracking()
            .AnyAsync(wf => wf.Id == createdId, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            sendFeedbackResult.Should().BeSuccess();
            feedbackExists.Should().BeTrue();
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
        var sendFeedbackResult =
            await _sendFeedbackCommandHandler.HandleAsync(
                sendFeedbackCommand,
                TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            sendFeedbackResult.IsFailed.Should().BeTrue();
            sendFeedbackResult.Errors.Should().NotBeEmpty();
            var validationError = sendFeedbackResult.Errors[0] as ValidationError;
            validationError.Should().NotBeNull();
            validationError!.ErrorCode.Should().Be("Feedback.OutOfRange");
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
        var sendFeedbackResult =
            await _sendFeedbackCommandHandler.HandleAsync(
                sendFeedbackCommand,
                TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            sendFeedbackResult.Should().BeFailure();
            sendFeedbackResult.Errors.Should().ContainSingle();
            var validationError = sendFeedbackResult.Errors[0] as ValidationError;
            validationError.Should().NotBeNull();
            validationError!.ErrorCode.Should().Be("Feedback.FeedbackRequired");
        }
    }

    [Fact]
    public async Task WhenAllPropertiesInvalid_ReturnsValidationErrorForAll()
    {
        // Arrange
        var sendFeedbackCommand = new SendFeedbackCommand
        {
            Feedback = new string('a', 501),
            Rating = 50,
            UserId = Guid.CreateVersion7()
        };

        // Act
        var sendFeedbackResult =
            await _sendFeedbackCommandHandler.HandleAsync(
                sendFeedbackCommand,
                TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            sendFeedbackResult.Should().BeFailure();
            sendFeedbackResult.Errors.Should().HaveCount(2);
            sendFeedbackResult.Errors.Should().AllBeAssignableTo<ValidationError>();
            var errors = sendFeedbackResult.Errors.OfType<ValidationError>().ToList();
            errors.Should().HaveCount(2);
            errors.Should()
                .ContainSingle(err => err.ErrorCode == "Feedback.TextTooLong")
                .And.ContainSingle(err => err.ErrorCode == "Feedback.RatingOutOfRange");
        }
    }
}
