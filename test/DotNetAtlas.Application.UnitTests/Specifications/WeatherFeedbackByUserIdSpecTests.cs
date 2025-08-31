using Ardalis.Specification.EntityFrameworkCore;
using AwesomeAssertions;
using AwesomeAssertions.Execution;
using DotNetAtlas.Application.Common.Specifications;
using DotNetAtlas.Domain.Entities.Weather;

namespace DotNetAtlas.Application.UnitTests.Specifications;

public class WeatherFeedbackByUserIdSpecTests
{
    [Fact]
    public void WhenApplied_ShouldFilterByUserId()
    {
        // Arrange
        var matchUser = Guid.CreateVersion7();
        var specToTest = new WeatherFeedbackByUserIdSpec(matchUser);
        var otherUser = Guid.CreateVersion7();
        var weatherFeedbacks = new List<WeatherFeedback>
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
