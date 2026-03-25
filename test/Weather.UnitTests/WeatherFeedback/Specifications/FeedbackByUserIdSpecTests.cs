using Ardalis.Specification.EntityFrameworkCore;
using Weather.Domain.Feedback.Specifications;
using Weather.Domain.Feedback.ValueObjects;

namespace Weather.UnitTests.WeatherFeedback.Specifications;

public class FeedbackByUserIdSpecTests
{
    [Fact]
    public void WhenApplied_ShouldFilterByUserId()
    {
        // Arrange
        var targetUserId = Guid.CreateVersion7();
        var otherUserId = Guid.CreateVersion7();

        var targetUserFeedback = Domain.Feedback.Feedback.Create(
            FeedbackText.Create("a").Value,
            FeedbackRating.Create(3).Value,
            targetUserId).Value;
        var otherUserFeedback = Domain.Feedback.Feedback.Create(
            FeedbackText.Create("b").Value,
            FeedbackRating.Create(4).Value,
            otherUserId).Value;

        var feedbackByUserIdSpec = new FeedbackByUserIdSpec(targetUserId);
        var feedbacks = new List<Domain.Feedback.Feedback>
        {
            targetUserFeedback,
            otherUserFeedback
        };

        // Act
        var filteredFeedbacks = feedbacks
            .AsQueryable()
            .WithSpecification(feedbackByUserIdSpec)
            .ToList();

        // Assert
        using (new AssertionScope())
        {
            filteredFeedbacks.Should().ContainSingle();
            filteredFeedbacks.Single().CreatedByUser.Should().Be(targetUserId);
        }
    }
}
