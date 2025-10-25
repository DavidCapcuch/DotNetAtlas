using System.ComponentModel.DataAnnotations.Schema;
using DotNetAtlas.Domain.Common;
using DotNetAtlas.Domain.Entities.Weather.Feedback.Errors;
using FluentResults;

namespace DotNetAtlas.Domain.Entities.Weather.Feedback.ValueObjects;

[ComplexType]
public record FeedbackText : ValueObject
{
    public string Value { get; private set; } = null!;

    private FeedbackText()
    {
    }

    public static Result<FeedbackText> Create(string? feedback)
    {
        feedback = feedback?.Trim();

        var result = Result.Merge(
            Result.FailIf(string.IsNullOrWhiteSpace(feedback), FeedbackErrors.FeedbackRequired()),
            Result.FailIf(feedback?.Length > 500, FeedbackErrors.FeedbackTooLong(500)));

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
