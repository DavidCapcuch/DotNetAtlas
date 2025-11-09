using Bogus;
using Weather.Forecast;

namespace DotNetAtlas.OutboxRelay.Benchmark.Seed;

/// <summary>
/// Bogus faker for generating realistic ForecastRequestedEvent Avro messages.
/// Generates only the Avro event - serialization to OutboxMessage happens externally.
/// </summary>
public sealed class ForecastRequestedEventFaker : Faker<ForecastRequestedEvent>
{
    private static readonly string[] Cities =
    [
        "Prague", "London", "Paris", "Berlin", "Madrid",
        "Rome", "Vienna", "Warsaw", "Budapest", "Amsterdam",
        "Brussels", "Copenhagen", "Stockholm", "Oslo", "Helsinki",
        "Dublin", "Lisbon", "Athens", "Bucharest", "Sofia"
    ];

    private static readonly CountryCode[] CountryCodes =
    [
        CountryCode.CZ, CountryCode.GB, CountryCode.FR, CountryCode.DE, CountryCode.ES,
        CountryCode.IT, CountryCode.AT, CountryCode.PL, CountryCode.HU, CountryCode.NL,
        CountryCode.BE, CountryCode.DK, CountryCode.SE, CountryCode.NO, CountryCode.FI,
        CountryCode.IE, CountryCode.PT, CountryCode.GR, CountryCode.RO, CountryCode.BG
    ];

    public ForecastRequestedEventFaker()
    {
        RuleFor(f => f.City, f => f.PickRandom(Cities));
        RuleFor(f => f.CountryCode, (f, eventObj) =>
        {
            var cityIndex = Array.IndexOf(Cities, eventObj.City);
            return cityIndex >= 0 ? CountryCodes[cityIndex] : f.PickRandom(CountryCodes);
        });
        RuleFor(f => f.Days, f => f.Random.Int(1, 14));
        RuleFor(f => f.UserId, f => f.Random.Guid());
        RuleFor(f => f.EventId, f => f.Random.Guid());
        RuleFor(f => f.OccurredOnUtc, f => DateTime.UtcNow.AddMinutes(-f.Random.Int(0, 1000)));
    }
}
