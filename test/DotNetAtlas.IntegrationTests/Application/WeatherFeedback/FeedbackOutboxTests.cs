using DotNetAtlas.Application.WeatherFeedback.ChangeFeedback;
using DotNetAtlas.Application.WeatherFeedback.SendFeedback;
using DotNetAtlas.IntegrationTests.Common;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.IntegrationTests.Application.WeatherFeedback;

[Collection<ForecastTestCollection>]
public class FeedbackOutboxTests : BaseIntegrationTest
{
    private readonly SendFeedbackCommandHandler _sendFeedbackCommandHandler;
    private readonly ChangeFeedbackCommandHandler _changeFeedbackCommandHandler;

    public FeedbackOutboxTests(IntegrationTestFixture app)
        : base(app)
    {
        _sendFeedbackCommandHandler =
            new SendFeedbackCommandHandler(
                Scope.ServiceProvider.GetRequiredService<ILogger<SendFeedbackCommandHandler>>(),
                WeatherDbContext);
        _changeFeedbackCommandHandler =
            new ChangeFeedbackCommandHandler(
                Scope.ServiceProvider.GetRequiredService<ILogger<ChangeFeedbackCommandHandler>>(),
                WeatherDbContext);
    }

    [Fact]
    public async Task WhenFeedbackCreatedAndChanged_PublishesBothDomainEventsViaOutbox()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var sendFeedbackCommand = new SendFeedbackCommand
        {
            Feedback = "Excellent weather forecast!",
            Rating = 5,
            UserId = userId
        };

        // Act - Create Feedback
        var sendFeedbackResult = await _sendFeedbackCommandHandler.HandleAsync(
            sendFeedbackCommand,
            TestContext.Current.CancellationToken);
        sendFeedbackResult.Should().BeSuccess();
        var createdId = sendFeedbackResult.Value;

        var createdFeedback = await WeatherDbContext.Feedbacks
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == createdId, TestContext.Current.CancellationToken);
        createdFeedback.Should().NotBeNull();

        // Change the feedback
        var changeFeedbackCommand = new ChangeFeedbackCommand
        {
            Id = createdId,
            Feedback = "Updated weather forecast feedback!",
            Rating = 4,
            UserId = userId
        };

        var changeFeedbackResult = await _changeFeedbackCommandHandler.HandleAsync(
            changeFeedbackCommand,
            TestContext.Current.CancellationToken);
        changeFeedbackResult.Should().BeSuccess();

        var last2OutboxMessages = await WeatherDbContext.OutboxMessages
            .AsNoTracking()
            .OrderByDescending(om => om.Id)
            .Take(2)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            last2OutboxMessages.Should().HaveCount(2);
            last2OutboxMessages[1].KafkaKey.Should().Be(createdId.ToString());
            last2OutboxMessages[1].Type.Should().Be("FeedbackCreatedEvent");
            last2OutboxMessages[0].KafkaKey.Should().Be(createdId.ToString());
            last2OutboxMessages[0].Type.Should().Be("FeedbackChangedEvent");
        }
    }
}
