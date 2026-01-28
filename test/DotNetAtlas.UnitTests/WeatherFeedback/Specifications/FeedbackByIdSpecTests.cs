using Ardalis.Specification.EntityFrameworkCore;
using DotNetAtlas.Domain.Feedback;
using DotNetAtlas.Domain.Feedback.Specifications;
using DotNetAtlas.Domain.Feedback.ValueObjects;

namespace DotNetAtlas.UnitTests.WeatherFeedback.Specifications;

public class FeedbackByIdSpecTests
{
    [Fact]
    public void WhenApplied_ShouldFilterById()
    {
        // Arrange
        var targetFeedback = Feedback.Create(
            FeedbackText.Create("a").Value,
            FeedbackRating.Create(3).Value,
            Guid.CreateVersion7()).Value;
        var otherFeedback = Feedback.Create(
            FeedbackText.Create("b").Value,
            FeedbackRating.Create(4).Value,
            Guid.CreateVersion7()).Value;

        var feedbackByIdSpec = new FeedbackByIdSpec(targetFeedback.Id);
        var feedbacks = new List<Feedback>
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
