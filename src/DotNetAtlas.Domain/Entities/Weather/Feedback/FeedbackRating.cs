using System.ComponentModel.DataAnnotations.Schema;
using DotNetAtlas.Domain.Entities.Base;
using DotNetAtlas.Domain.Errors;
using FluentResults;

namespace DotNetAtlas.Domain.Entities.Weather.Feedback;

[ComplexType]
public record FeedbackRating : ValueObject
{
    public int Value { get; private set; }

    private FeedbackRating()
    {
    }

    public static Result<FeedbackRating> Create(int value)
    {
        if (value is < 1 or > 5)
        {
            return Result.Fail(FeedbackRatingErrors.OutOfRange(1, 5));
        }

        return new FeedbackRating
        {
            Value = value
        };
    }
}
