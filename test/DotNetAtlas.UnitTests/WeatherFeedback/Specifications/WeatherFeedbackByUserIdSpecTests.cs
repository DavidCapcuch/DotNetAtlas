using Ardalis.Specification.EntityFrameworkCore;
using DotNetAtlas.Application.WeatherFeedback.Common.Specifications;
using DotNetAtlas.Domain.Entities.Weather.Feedback;
using DotNetAtlas.Domain.Entities.Weather.Feedback.ValueObjects;

namespace DotNetAtlas.UnitTests.WeatherFeedback.Specifications;

public class WeatherFeedbackByUserIdSpecTests
{
    [Fact]
    public void WhenApplied_ShouldFilterByUserId()
    {
        // Arrange
        var matchUser = Guid.CreateVersion7();
        var specToTest = new WeatherFeedbackByUserIdSpec(matchUser);
        var otherUser = Guid.CreateVersion7();
        var weatherFeedbacks = new List<Feedback>
        {
            new(FeedbackText.Create("a").Value, FeedbackRating.Create(3).Value, matchUser),
            new(FeedbackText.Create("b").Value, FeedbackRating.Create(4).Value, otherUser)
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
            result.Single().CreatedByUser.Should().Be(matchUser);
        }
    }
}
