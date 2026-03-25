using FluentResults;
using Platform.SharedKernel.Base;
using Weather.Domain.Common.Errors;

namespace Weather.Domain.Common.ValueObjects;

/// <summary>
/// Represents a validated city name (trimmed, limited length).
/// </summary>
public sealed record City : ValueObject
{
    public const int MinLength = 2;
    public const int MaxLength = 100;

    public string Name { get; private init; } = null!;

    private City()
    {
    }

    public static Result<City> Create(string? city)
    {
        city = city?.Trim();

        var mergedResults = Result.Merge(
            Result.FailIf(string.IsNullOrWhiteSpace(city), CityErrors.InvalidCity()),
            Result.FailIf(city?.Length is > MaxLength or < MinLength,
                CityErrors.CityOutOfRange(MinLength, MaxLength)));

        if (mergedResults.IsFailed)
        {
            return mergedResults;
        }

        return new City
        {
            Name = city!
        };
    }
}
