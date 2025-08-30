using Ardalis.Specification.EntityFrameworkCore;
using AwesomeAssertions;
using AwesomeAssertions.Execution;
using DotNetAtlas.Application.Common.Specifications;
using DotNetAtlas.Domain.Entities.Weather;

namespace DotNetAtlas.Application.UnitTests.Specifications
{
    public class WeatherFeedbackByIdSpecTests
    {
        [Fact]
        public void WhenApplied_ShouldFilterById()
        {
            // Arrange
            var matchFeedback = new WeatherFeedback(
                FeedbackText.Create("a").Value,
                FeedbackRating.Create(3).Value,
                Guid.CreateVersion7());
            var specToTest = new WeatherFeedbackByIdSpec(matchFeedback.Id);
            var weatherFeedbacks = new List<WeatherFeedback>
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
                result.Should().HaveCount(1);
                result.Single().Id.Should().Be(matchFeedback.Id);
            }
        }
    }
}