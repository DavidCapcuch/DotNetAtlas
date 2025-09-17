using DotNetAtlas.Application.Forecast.GetForecasts;

namespace DotNetAtlas.Application.Forecast.Services.Requests;

public sealed record ForecastRequest(
    string City,
    CountryCode CountryCode,
    int Days);
