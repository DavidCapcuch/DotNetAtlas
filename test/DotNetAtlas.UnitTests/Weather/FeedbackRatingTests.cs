using AwesomeAssertions;
using AwesomeAssertions.Execution;
using DotNetAtlas.Domain.Entities.Weather.Feedback;
using DotNetAtlas.Domain.Errors.Base;
using FluentResults.Extensions.FluentAssertions;

namespace DotNetAtlas.UnitTests.Weather;

public class FeedbackRatingTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void WhenValueWithinRange_ReturnsSuccessWithValue(int value)
    {
        // Arrange & Act
        var result = FeedbackRating.Create(value);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Value.Should().Be(value);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-10)]
    public void WhenValueOutOfRange_ReturnsValidationError(int value)
    {
        // Arrange & Act
        var result = FeedbackRating.Create(value);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            var validationError = result.Errors[0] as ValidationError;
            validationError.Should().NotBeNull();
            validationError!.ErrorCode.Should().Be("FeedbackRating.OutOfRange");
        }
    }
}
