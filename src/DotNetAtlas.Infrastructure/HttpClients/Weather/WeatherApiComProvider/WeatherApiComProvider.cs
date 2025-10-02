using System.Globalization;
using System.Net.Http.Json;
using DotNetAtlas.Application.Forecast;
using DotNetAtlas.Application.Forecast.GetForecasts;
using DotNetAtlas.Application.Forecast.Services.Abstractions;
using DotNetAtlas.Application.Forecast.Services.Requests;
using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Infrastructure.HttpClients.Weather.WeatherApiComProvider;

public class WeatherApiComProvider : IWeatherForecastProvider
{
    public const string HttpClientName = "weatherapi-com";
    public string Name => "WeatherAPI.com";

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly IGeocodingService _geocodingService;

    public WeatherApiComProvider(
        [FromKeyedServices(HttpClientName)] HttpClient httpClient,
        IOptions<WeatherApiComOptions> options,
        [FromKeyedServices(WeatherApiComGeocodingService.ServiceKey)]
        IGeocodingService geocodingService)
    {
        _httpClient = httpClient;
        _apiKey = options.Value.ApiKey;
        _geocodingService = geocodingService;
    }

    public async Task<Result<IReadOnlyList<ForecastDto>>> GetForecastAsync(
        ForecastRequest forecastRequest,
        CancellationToken ct)
    {
        var geoRequest = forecastRequest.ToGeocodingRequest();
        var geoResult = await _geocodingService.GetCoordinatesAsync(geoRequest, ct);
        if (geoResult.IsFailed)
        {
            return Result.Fail(geoResult.Errors);
        }

        var geoCoordinates = geoResult.Value;
        var query = $"v1/forecast.json" +
                $"?key={_apiKey}" +
                $"&q={geoCoordinates.Latitude},{geoCoordinates.Longitude}" +
                $"&days={forecastRequest.Days}" +
                $"&aqi=no" +
                $"&alerts=no";

        var forecastResponse = await _httpClient.GetFromJsonAsync<WeatherApiForecastResponse>(query, ct);
        if (forecastResponse?.Forecast?.Forecastday is null)
        {
            throw new InvalidOperationException("WeatherAPI.com forecast not available");
        }

        var forecastDtos = new List<ForecastDto>();
        foreach (var day in forecastResponse.Forecast.Forecastday)
        {
            ct.ThrowIfCancellationRequested();
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
