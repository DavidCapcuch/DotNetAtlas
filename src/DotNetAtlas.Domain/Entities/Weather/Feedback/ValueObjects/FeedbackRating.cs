using System.ComponentModel.DataAnnotations.Schema;
using DotNetAtlas.Domain.Common;
using DotNetAtlas.Domain.Entities.Weather.Feedback.Errors;
using FluentResults;

namespace DotNetAtlas.Domain.Entities.Weather.Feedback.ValueObjects;

[ComplexType]
public record FeedbackRating : ValueObject
{
    public const int MinimumRating = 1;
    public const int MaximumRating = 5;

    public int Value { get; private set; }

    private FeedbackRating()
    {
    }

    public static Result<FeedbackRating> Create(int value)
    {
        if (value is < MinimumRating or > MaximumRating)
        {
            return Result.Fail(FeedbackRatingErrors.OutOfRange(MinimumRating, MaximumRating));
        }

        return new FeedbackRating
        {
            Value = value
        };
    }
}
