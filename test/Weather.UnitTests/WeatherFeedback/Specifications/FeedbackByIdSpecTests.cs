using Ardalis.Specification.EntityFrameworkCore;
using Weather.Domain.Feedback.Specifications;
using Weather.Domain.Feedback.ValueObjects;
using Weather.UnitTests.Common;

namespace Weather.UnitTests.WeatherFeedback.Specifications;

public class FeedbackByIdSpecTests
{
    [Fact]
    public void WhenApplied_ShouldFilterById()
    {
        // Arrange
        var targetFeedback = Domain.Feedback.Feedback.Create(
            FeedbackText.Create("a").Value,
            FeedbackRating.Create(3).Value,
            Guid.CreateVersion7(),
            TestInstants.FixedNow).Value;
        var otherFeedback = Domain.Feedback.Feedback.Create(
            FeedbackText.Create("b").Value,
            FeedbackRating.Create(4).Value,
            Guid.CreateVersion7(),
            TestInstants.FixedNow).Value;

        var feedbackByIdSpec = new FeedbackByIdSpec(targetFeedback.Id);
        var feedbacks = new List<Domain.Feedback.Feedback>
        {
            targetFeedback,
            otherFeedback,
        };

        // Act
        var filteredFeedbacks = feedbacks
            .AsQueryable()
            .WithSpecification(feedbackByIdSpec)
            .ToList();

        // Assert
        using (new AssertionScope())
        {
            filteredFeedbacks.Should().ContainSingle();
            filteredFeedbacks.Single().Id.Should().Be(targetFeedback.Id);
        }
    }
}
