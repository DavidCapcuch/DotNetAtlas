using System.ComponentModel.DataAnnotations.Schema;
using DotNetAtlas.Domain.Entities.Base;
using DotNetAtlas.Domain.Errors;
using FluentResults;

namespace DotNetAtlas.Domain.Entities.Weather.Feedback;

[ComplexType]
public record FeedbackText : ValueObject
{
    public string Value { get; private set; } = null!;

    private FeedbackText()
    {
    }

    public static Result<FeedbackText> Create(string feedback)
    {
        feedback = feedback?.Trim();

        var result = Result.Merge(
            Result.FailIf(string.IsNullOrWhiteSpace(feedback), WeatherFeedbackErrors.FeedbackRequired()),
            Result.FailIf(feedback?.Length > 500, WeatherFeedbackErrors.FeedbackTooLong(500)));

        if (result.IsFailed)
        {
            return result;
        }

        return new FeedbackText
        {
            Value = feedback!
        };
    }
}
