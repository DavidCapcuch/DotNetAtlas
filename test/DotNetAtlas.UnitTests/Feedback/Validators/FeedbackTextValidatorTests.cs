using DotNetAtlas.Application.Feedback.Common.Validation;
using FluentValidation.TestHelper;

namespace DotNetAtlas.UnitTests.Feedback.Validators;

public class FeedbackTextValidatorTests
{
    private readonly FeedbackTextValidator _feedbackTextValidator = new();

    [Fact]
    public void GivenNonEmptyAndWithinLimit_ShouldPass()
    {
        // Arrange
        var text = new string('a', 100);

        // Act
        var result = _feedbackTextValidator.TestValidate(text);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void GivenEmpty_ShouldFail()
    {
        // Arrange
        var text = string.Empty;

        // Act
        var result = _feedbackTextValidator.TestValidate(text);

        // Assert
        result.ShouldHaveValidationErrors();
    }

    [Fact]
    public void GivenTooLong_ShouldFail()
    {
        // Arrange
        var text = new string('a', 501);

        // Act
        var result = _feedbackTextValidator.TestValidate(text);

        // Assert
        result.ShouldHaveValidationErrors();
    }
}
