using System.Globalization;
using System.Net.Http.Json;
using DotNetAtlas.Application.WeatherForecast.GetForecasts;
using DotNetAtlas.Application.WeatherForecast.Services.Abstractions;
using DotNetAtlas.Domain.Common.Services;
using DotNetAtlas.Domain.Forecast.ValueObjects;
using FluentResults;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetAtlas.Infrastructure.HttpClients.WeatherProviders.OpenMeteo;

public class OpenMeteoWeatherProvider : IMainWeatherForecastProvider
{
    public const string HttpClientName = "open-meteo";
    public string Name => "Open-Meteo";

    private readonly HttpClient _httpClient;
    private readonly IGeocodingProvider _geocodingProvider;

    public OpenMeteoWeatherProvider(
        [FromKeyedServices(HttpClientName)] HttpClient httpClient,
        [FromKeyedServices(OpenMeteoGeocodingProvider.ServiceKey)]
        IGeocodingProvider geocodingProvider)
    {
        _httpClient = httpClient;
        _geocodingProvider = geocodingProvider;
    }

    public async Task<Result<IReadOnlyList<ForecastDto>>> GetForecastAsync(
        ForecastCriteria criteria,
        CancellationToken ct)
    {
        var geoResult = await _geocodingProvider.GetCoordinatesAsync(criteria.City, criteria.CountryCode, ct);
        if (geoResult.IsFailed)
        {
            return Result.Fail(geoResult.Errors);
        }

        var geoCoordinates = geoResult.Value;

        var queryString = $"v1/forecast" +
                          $"?latitude={geoCoordinates.Latitude.ToString(CultureInfo.InvariantCulture)}" +
                          $"&longitude={geoCoordinates.Longitude.ToString(CultureInfo.InvariantCulture)}" +
                          $"&daily=temperature_2m_max,temperature_2m_min" +
                          $"&timezone=UTC" +
                          $"&start_date={criteria.DateRange.StartDateOnly:yyyy-MM-dd}" +
                          $"&end_date={criteria.DateRange.EndDateOnly:yyyy-MM-dd}";

        var forecastResponse = await _httpClient.GetFromJsonAsync<OpenMeteoForecastResponse>(queryString, ct);
        if (forecastResponse?.Daily is null || forecastResponse.Daily.Time.Length == 0)
        {
            throw new InvalidOperationException("Open-Meteo forecast not available");
        }

        var count = forecastResponse.Daily.Time.Length;
        var forecastDtos = new List<ForecastDto>(count);
        for (var i = 0; i < count; i++)
        {
            forecastDtos.Add(new ForecastDto
            {
                Date = DateOnly.FromDateTime(
                    DateTime.ParseExact(forecastResponse.Daily.Time[i], "yyyy-MM-dd", CultureInfo.InvariantCulture)),
                MaxTemperatureC = forecastResponse.Daily.TemperatureMax[i],
                MinTemperatureC = forecastResponse.Daily.TemperatureMin[i],
                Summary = null
            });
        }

        return forecastDtos;
    }
}
