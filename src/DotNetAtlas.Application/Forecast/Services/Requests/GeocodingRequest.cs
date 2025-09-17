using DotNetAtlas.Application.Forecast.GetForecasts;

namespace DotNetAtlas.Application.Forecast.Services.Requests;

public sealed record GeocodingRequest(
    string City,
    CountryCode CountryCode);
