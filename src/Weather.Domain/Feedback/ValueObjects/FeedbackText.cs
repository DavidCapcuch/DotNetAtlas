using FluentResults;
using Platform.SharedKernel.Base;
using Weather.Domain.Feedback.Errors;

namespace Weather.Domain.Feedback.ValueObjects;

public sealed record FeedbackText : ValueObject
{
    public const int TextMaxLength = 500;

    public string Text { get; private init; } = null!;

    private FeedbackText()
    {
    }

    public static Result<FeedbackText> Create(string? feedback)
    {
        feedback = feedback?.Trim();

        var mergedResults = Result.Merge(
            Result.FailIf(string.IsNullOrWhiteSpace(feedback), FeedbackErrors.FeedbackRequired()),
            Result.FailIf(feedback?.Length > TextMaxLength, FeedbackErrors.FeedbackTooLong(TextMaxLength)));

        if (mergedResults.IsFailed)
        {
            return Result.Fail<FeedbackText>(mergedResults.Errors);
        }

        return new FeedbackText
        {
            Text = feedback!
        };
    }
}
