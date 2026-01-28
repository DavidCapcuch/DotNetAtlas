using DotNetAtlas.Domain.Common.ValueObjects;
using DotNetAtlas.Domain.Forecast.Errors;
using DotNetAtlas.SharedKernel.Base;
using FluentResults;

namespace DotNetAtlas.Domain.Forecast.ValueObjects;

/// <summary>
/// Captures the intent for fetching forecasts using the ubiquitous language while enforcing core invariants.
/// </summary>
public sealed record ForecastCriteria : ValueObject
{
    public const int MinDays = 1;
    public const int MaxDays = 14;

    public CountryCode CountryCode { get; private init; }

    public City City { get; private init; }

    public DateRange DateRange { get; private init; }

    public int Days => DateRange.LengthInDays;

    private ForecastCriteria()
    {
    }

    public static Result<ForecastCriteria> Create(
        string? city,
        CountryCode countryCode,
        DateRange dateRange)
    {
        var cityResult = City.Create(city);

        var mergedResults = Result.Merge(
            cityResult,
            Result.FailIf(dateRange.LengthInDays is < MinDays or > MaxDays,
                ForecastErrors.InvalidDaysRange(MinDays, MaxDays, dateRange.LengthInDays)));

        if (mergedResults.IsFailed)
        {
            return Result.Fail(mergedResults.Errors);
        }

        return new ForecastCriteria
        {
            City = cityResult.Value,
            CountryCode = countryCode,
            DateRange = dateRange
        };
    }
}
