using Ardalis.Specification.EntityFrameworkCore;
using DotNetAtlas.Application.WeatherFeedback.Common.Specifications;
using DotNetAtlas.Domain.Entities.Weather.Feedback;
using DotNetAtlas.Domain.Entities.Weather.Feedback.ValueObjects;

namespace DotNetAtlas.UnitTests.WeatherFeedback.Specifications;

public class WeatherFeedbackByIdSpecTests
{
    [Fact]
    public void WhenApplied_ShouldFilterById()
    {
        // Arrange
        var matchFeedback = new Feedback(
            FeedbackText.Create("a").Value,
            FeedbackRating.Create(3).Value,
            Guid.CreateVersion7());
        var specToTest = new WeatherFeedbackByIdSpec(matchFeedback.Id);
        var weatherFeedbacks = new List<Feedback>
        {
            matchFeedback,
            new(FeedbackText.Create("b").Value, FeedbackRating.Create(4).Value, Guid.CreateVersion7()),
        };

        // Act
        var result = weatherFeedbacks
            .AsQueryable()
            .WithSpecification(specToTest)
            .ToList();

        // Assert
        using (new AssertionScope())
        {
            result.Should().ContainSingle();
            result.Single().Id.Should().Be(matchFeedback.Id);
        }
    }
}
