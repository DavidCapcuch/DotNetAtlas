using AwesomeAssertions;
using AwesomeAssertions.Execution;
using DotNetAtlas.Domain.Entities.Weather;
using DotNetAtlas.Domain.Errors.Base;
using FluentResults.Extensions.FluentAssertions;

namespace DotNetAtlas.Domain.UnitTests.Weather;

public class FeedbackTextTests
{
    [Theory]
    [InlineData("Great!")]
    [InlineData("  Nice job  ")]
    [InlineData("Thanks for the forecast")]
    public void WhenTextValid_ReturnsSuccessAndTrims(string input)
    {
        // Arrange & Act
        var result = FeedbackText.Create(input);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Value.Should().Be(input.Trim());
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WhenTextEmpty_ReturnsValidationError(string input)
    {
        // Arrange & Act
        var result = FeedbackText.Create(input);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            var validationError = result.Errors[0] as ValidationError;
            validationError.Should().NotBeNull();
            validationError!.ErrorCode.Should().Be("WeatherFeedback.FeedbackRequired");
        }
    }

    [Fact]
    public void WhenTextTooLong_ReturnsValidationError()
    {
        // Arrange
        var input = new string('a', 501);

        // Act
        var result = FeedbackText.Create(input);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            var validationError = result.Errors[0] as ValidationError;
            validationError.Should().NotBeNull();
            validationError!.ErrorCode.Should().Be("WeatherFeedback.FeedbackTooLong");
        }
    }
}
