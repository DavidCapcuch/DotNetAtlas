using System.Globalization;
using System.Net.Http.Json;
using DotNetAtlas.Application.WeatherForecast.GetForecasts;
using DotNetAtlas.Application.WeatherForecast.Services.Abstractions;
using DotNetAtlas.Application.WeatherForecast.Services.Requests;
using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using WeatherForecastMapper = DotNetAtlas.Application.WeatherForecast.WeatherForecastMapper;

namespace DotNetAtlas.Infrastructure.HttpClients.WeatherProviders.OpenMeteo;

public class OpenMeteoWeatherProvider : IMainWeatherForecastProvider
{
    public const string HttpClientName = "open-meteo";
    public string Name => "Open-Meteo";

    private readonly HttpClient _httpClient;
    private readonly IGeocodingService _geocodingService;

    public OpenMeteoWeatherProvider(
        [FromKeyedServices(HttpClientName)] HttpClient httpClient,
        [FromKeyedServices(OpenMeteoGeocodingService.ServiceKey)]
        IGeocodingService geocodingService)
    {
        _httpClient = httpClient;
        _geocodingService = geocodingService;
    }

    public async Task<Result<IReadOnlyList<ForecastDto>>> GetForecastAsync(
        ForecastRequest forecastRequest,
        CancellationToken ct)
    {
        var geoRequest = WeatherForecastMapper.ToGeocodingRequest(forecastRequest);
        var geoResult = await _geocodingService.GetCoordinatesAsync(geoRequest, ct);
        if (geoResult.IsFailed)
        {
            return Result.Fail(geoResult.Errors);
        }

        var geoCoordinates = geoResult.Value;

        var startDate = DateTime.UtcNow.Date;
        var endDate = startDate.AddDays(forecastRequest.Days - 1);
        var query = $"v1/forecast" +
                    $"?latitude={geoCoordinates.Latitude}" +
                    $"&longitude={geoCoordinates.Longitude}" +
                    $"&daily=temperature_2m_max,temperature_2m_min" +
                    $"&timezone=UTC" +
                    $"&start_date={startDate:yyyy-MM-dd}" +
                    $"&end_date={endDate:yyyy-MM-dd}";

        var forecastResponse = await _httpClient.GetFromJsonAsync<OpenMeteoForecastResponse>(query, ct);
        if (forecastResponse?.Daily is null || forecastResponse.Daily.Time.Length == 0)
        {
            throw new InvalidOperationException("Open-Meteo forecast not available");
        }

        var count = forecastResponse.Daily.Time.Length;
        var forecastDtos = new List<ForecastDto>(count);
        for (var i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
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
