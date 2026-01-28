using DotNetAtlas.Domain.Feedback.Errors;
using DotNetAtlas.SharedKernel.Base;
using FluentResults;

namespace DotNetAtlas.Domain.Feedback.ValueObjects;

public sealed record FeedbackRating : ValueObject
{
    public const byte MinimumRating = 1;
    public const byte MaximumRating = 5;

    public byte Value { get; private init; }

    private FeedbackRating()
    {
    }

    public static Result<FeedbackRating> Create(byte value)
    {
        if (value is < MinimumRating or > MaximumRating)
        {
            return Result.Fail(FeedbackErrors.OutOfRange(MinimumRating, MaximumRating));
        }

        return new FeedbackRating
        {
            Value = value
        };
    }
}
