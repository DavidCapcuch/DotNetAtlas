using DotNetAtlas.Domain.Common.Errors;
using DotNetAtlas.SharedKernel.Base;
using FluentResults;

namespace DotNetAtlas.Domain.Common.ValueObjects;

/// <summary>
/// Captures a contiguous date range used when requesting forecasts.
/// </summary>
public sealed record DateRange : ValueObject
{
    public DateOnly StartDateOnly { get; private init; }

    public DateOnly EndDateOnly { get; private init; }

    public int LengthInDays => EndDateOnly.DayNumber - StartDateOnly.DayNumber + 1;

    private DateRange()
    {
    }

    /// <summary>
    /// Creates a DateRange starting from the given date spanning the specified number of days.
    /// </summary>
    /// <param name="start">The first day of the range (inclusive).</param>
    /// <param name="numberOfDays">Total days in the range (minimum 1).</param>
    public static Result<DateRange> Create(DateOnly start, int numberOfDays)
    {
        if (numberOfDays < 1)
        {
            return Result.Fail(DateRangeErrors.InvalidDaysCount(numberOfDays));
        }

        var end = start.AddDays(numberOfDays - 1);
        return new DateRange
        {
            StartDateOnly = start,
            EndDateOnly = end
        };
    }

    public static Result<DateRange> Create(DateOnly start, DateOnly end)
    {
        if (end < start)
        {
            return Result.Fail(DateRangeErrors.InvalidDateRange(start, end));
        }

        return new DateRange
        {
            StartDateOnly = start,
            EndDateOnly = end
        };
    }
}
