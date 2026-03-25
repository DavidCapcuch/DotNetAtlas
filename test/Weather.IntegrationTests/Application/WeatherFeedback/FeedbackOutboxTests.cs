using FluentResults.Extensions.FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQS;
using Weather.Application.WeatherFeedback.ChangeFeedback;
using Weather.Application.WeatherFeedback.SendFeedback;
using Weather.IntegrationTests.Common;

namespace Weather.IntegrationTests.Application.WeatherFeedback;

[Collection<ForecastTestCollection>]
public class FeedbackOutboxTests : BaseIntegrationTest
{
    private readonly ICommandHandler<SendFeedbackCommand, Guid> _sendFeedbackCommandHandler;
    private readonly ICommandHandler<ChangeFeedbackCommand> _changeFeedbackCommandHandler;

    public FeedbackOutboxTests(IntegrationTestFixture app)
        : base(app)
    {
        _sendFeedbackCommandHandler =
            Scope.ServiceProvider.GetRequiredService<ICommandHandler<SendFeedbackCommand, Guid>>();
        _changeFeedbackCommandHandler =
            Scope.ServiceProvider.GetRequiredService<ICommandHandler<ChangeFeedbackCommand>>();
    }

    [Fact]
    public async Task WhenFeedbackCreated_PublishesCreatedEvent()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var command = new SendFeedbackCommand
        {
            Feedback = "Excellent weather forecast!",
            Rating = 5,
            UserId = userId
        };

        // Act
        var result = await _sendFeedbackCommandHandler.HandleAsync(
            command,
            TestContext.Current.CancellationToken);
        result.Should().BeSuccess();
        var feedbackId = result.Value;

        var outboxMessages = await WeatherDbContext.OutboxMessages
            .AsNoTracking()
            .Where(om => om.KafkaKey == feedbackId.ToString())
            .OrderBy(om => om.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            outboxMessages.Should().ContainSingle();
            outboxMessages[0].Type.Should().Be("Weather.Feedback.FeedbackCreatedEvent");
        }
    }

    [Fact]
    public async Task WhenFeedbackChanged_PublishesChangedEvent()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var createCommand = new SendFeedbackCommand
        {
            Feedback = "Excellent weather forecast!",
            Rating = 5,
            UserId = userId
        };

        var createResult = await _sendFeedbackCommandHandler.HandleAsync(
            createCommand,
            TestContext.Current.CancellationToken);
        createResult.Should().BeSuccess();
        var feedbackId = createResult.Value;

        var changeCommand = new ChangeFeedbackCommand
        {
            Id = feedbackId,
            Feedback = "Updated weather forecast feedback!",
            Rating = 4,
            UserId = userId
        };

        // Act
        var changeResult = await _changeFeedbackCommandHandler.HandleAsync(
            changeCommand,
            TestContext.Current.CancellationToken);
        changeResult.Should().BeSuccess();

        var outboxMessages = await WeatherDbContext.OutboxMessages
            .AsNoTracking()
            .Where(om => om.KafkaKey == feedbackId.ToString())
            .OrderBy(om => om.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            outboxMessages.Should().HaveCount(2);
            outboxMessages[0].Type.Should().Be("Weather.Feedback.FeedbackCreatedEvent");
            outboxMessages[1].Type.Should().Be("Weather.Feedback.FeedbackChangedEvent");
        }
    }
}
