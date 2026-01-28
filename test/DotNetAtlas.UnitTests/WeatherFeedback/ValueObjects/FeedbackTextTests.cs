using DotNetAtlas.Domain.Feedback.ValueObjects;
using DotNetAtlas.SharedKernel.Errors;
using FluentResults.Extensions.FluentAssertions;

namespace DotNetAtlas.UnitTests.WeatherFeedback.ValueObjects;

public class FeedbackTextTests
{
    [Theory]
    [InlineData("Great!")]
    [InlineData("  Nice job  ")]
    [InlineData("Thanks for the forecast")]
    public void WhenTextValid_ReturnsSuccessAndTrims(string feedbackTextInput)
    {
        // Arrange & Act
        var result = FeedbackText.Create(feedbackTextInput);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Text.Should().Be(feedbackTextInput.Trim());
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WhenTextEmpty_ReturnsValidationError(string feedbackTextInput)
    {
        // Arrange & Act
        var result = FeedbackText.Create(feedbackTextInput);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            var validationError = result.Errors[0] as ValidationError;
            validationError.Should().NotBeNull();
            validationError!.ErrorCode.Should().Be("Feedback.TextRequired");
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
            validationError!.ErrorCode.Should().Be("Feedback.TextTooLong");
        }
    }
}
