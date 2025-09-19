using System.Text.Json.Serialization;
using DotNetAtlas.Application.Common.Cache;
using DotNetAtlas.Domain.Entities.Weather.Forecast;

namespace DotNetAtlas.Application.Forecast.Services.Requests;

public sealed record ForecastRequest(
    string City,
    CountryCode CountryCode,
    int Days) : ICacheableItem
{
    [JsonIgnore]
    public string CacheKey => $"{nameof(ForecastRequest)}:{City.ToUpperInvariant()}:{CountryCode}:{Days}";
}
