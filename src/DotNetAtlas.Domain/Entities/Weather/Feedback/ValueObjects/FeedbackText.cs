using System.ComponentModel.DataAnnotations.Schema;
using DotNetAtlas.Domain.Common;
using DotNetAtlas.Domain.Entities.Weather.Feedback.Errors;
using FluentResults;

namespace DotNetAtlas.Domain.Entities.Weather.Feedback.ValueObjects;

[ComplexType]
public record FeedbackText : ValueObject
{
    public const int TextMaxLength = 500;

    public string Value { get; private set; } = null!;

    private FeedbackText()
    {
    }

    public static Result<FeedbackText> Create(string? feedback)
    {
        feedback = feedback?.Trim();

        var result = Result.Merge(
            Result.FailIf(string.IsNullOrWhiteSpace(feedback), FeedbackErrors.FeedbackRequired()),
            Result.FailIf(feedback?.Length > TextMaxLength, FeedbackErrors.FeedbackTooLong(TextMaxLength)));

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
