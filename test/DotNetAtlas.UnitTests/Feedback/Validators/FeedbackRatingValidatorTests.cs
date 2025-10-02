using DotNetAtlas.Application.Feedback.Common.Validation;
using FluentValidation.TestHelper;

namespace DotNetAtlas.UnitTests.Feedback.Validators;

public class FeedbackRatingValidatorTests
{
    private readonly FeedbackRatingValidator _feedbackRatingValidator = new();

    [Theory]
    [InlineData((byte)1)]
    [InlineData((byte)3)]
    [InlineData((byte)5)]
    public void GivenRatingInRange_ShouldPass(byte rating)
    {
        // Act
        var result = _feedbackRatingValidator.TestValidate(rating);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)6)]
    [InlineData((byte)66)]
    public void GivenRatingOutOfRange_ShouldFail(byte rating)
    {
        // Act
        var result = _feedbackRatingValidator.TestValidate(rating);

        // Assert
        result.ShouldHaveValidationErrors();
    }
}
