using DotNetAtlas.Domain.Entities.Weather.Forecast;

namespace DotNetAtlas.Application.Forecast.Services.Requests;

public sealed record GeocodingRequest(
    string City,
    CountryCode CountryCode);
