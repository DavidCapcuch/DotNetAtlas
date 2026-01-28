using Ardalis.Specification.EntityFrameworkCore;
using DotNetAtlas.Domain.Feedback;
using DotNetAtlas.Domain.Feedback.Specifications;
using DotNetAtlas.Domain.Feedback.ValueObjects;

namespace DotNetAtlas.UnitTests.WeatherFeedback.Specifications;

public class FeedbackByUserIdSpecTests
{
    [Fact]
    public void WhenApplied_ShouldFilterByUserId()
    {
        // Arrange
        var targetUserId = Guid.CreateVersion7();
        var otherUserId = Guid.CreateVersion7();

        var targetUserFeedback = Feedback.Create(
            FeedbackText.Create("a").Value,
            FeedbackRating.Create(3).Value,
            targetUserId).Value;
        var otherUserFeedback = Feedback.Create(
            FeedbackText.Create("b").Value,
            FeedbackRating.Create(4).Value,
            otherUserId).Value;

        var feedbackByUserIdSpec = new FeedbackByUserIdSpec(targetUserId);
        var feedbacks = new List<Feedback>
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
