using System.Globalization;
using System.Net.Http.Json;
using DotNetAtlas.Application.WeatherForecast.GetForecasts;
using DotNetAtlas.Application.WeatherForecast.Services.Abstractions;
using DotNetAtlas.Domain.Common.Services;
using DotNetAtlas.Domain.Forecast.ValueObjects;
using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Infrastructure.HttpClients.WeatherProviders.WeatherApiCom;

public class WeatherApiComProvider : IWeatherForecastProvider
{
    public const string HttpClientName = "weatherapi-com";
    public string Name => "WeatherAPI.com";

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly IGeocodingProvider _geocodingProvider;

    public WeatherApiComProvider(
        [FromKeyedServices(HttpClientName)] HttpClient httpClient,
        IOptions<WeatherApiComOptions> options,
        [FromKeyedServices(WeatherApiComGeocodingProvider.ServiceKey)]
        IGeocodingProvider geocodingProvider)
    {
        _httpClient = httpClient;
        _apiKey = options.Value.ApiKey;
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
        var queryString = $"v1/forecast.json" +
                $"?key={_apiKey}" +
                $"&q={geoCoordinates.Latitude},{geoCoordinates.Longitude}" +
                $"&days={criteria.Days}" +
                $"&aqi=no" +
                $"&alerts=no";

        var forecastResponse = await _httpClient.GetFromJsonAsync<WeatherApiComForecastResponse>(queryString, ct);
        if (forecastResponse?.Forecast?.Forecastday is null)
        {
            throw new InvalidOperationException("WeatherAPI.com forecast not available");
        }

        var forecastDtos = new List<ForecastDto>();
        foreach (var day in forecastResponse.Forecast.Forecastday)
        {
            forecastDtos.Add(new ForecastDto
            {
                Date = DateOnly.FromDateTime(DateTime.ParseExact(day.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture)),
                MaxTemperatureC = day.Day.MaxTempC,
                MinTemperatureC = day.Day.MinTempC,
                Summary = day.Day.Condition?.Text
            });
        }

        return forecastDtos;
    }
}
