using DotNetAtlas.Domain.Feedback.ValueObjects;
using DotNetAtlas.SharedKernel.Errors;
using FluentResults.Extensions.FluentAssertions;

namespace DotNetAtlas.UnitTests.WeatherFeedback.ValueObjects;

public class FeedbackRatingTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void WhenValueWithinRange_ReturnsSuccessWithValue(byte ratingValue)
    {
        // Arrange & Act
        var result = FeedbackRating.Create(ratingValue);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Value.Should().Be(ratingValue);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void WhenValueOutOfRange_ReturnsValidationError(byte ratingValue)
    {
        // Arrange & Act
        var result = FeedbackRating.Create(ratingValue);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            var validationError = result.Errors[0] as ValidationError;
            validationError.Should().NotBeNull();
            validationError!.ErrorCode.Should().Be("Feedback.RatingOutOfRange");
        }
    }
}
