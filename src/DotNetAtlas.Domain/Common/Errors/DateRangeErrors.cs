using DotNetAtlas.SharedKernel.Errors;

namespace DotNetAtlas.Domain.Common.Errors;

public static class DateRangeErrors
{
    public static ValidationError InvalidDateRange(DateOnly start, DateOnly end)
        => new ValidationError(
            propertyName: "DateRange",
            errorMessage: $"End date ({end:yyyy-MM-dd}) must be on or after start date ({start:yyyy-MM-dd}).",
            errorCode: "DateRange.InvalidDateRange");

    public static ValidationError InvalidDaysCount(int numberOfDays)
        => new ValidationError(
            propertyName: "NumberOfDays",
            errorMessage: $"Number of days must be at least 1. Provided: {numberOfDays}.",
            errorCode: "DateRange.InvalidDaysCount");
}
