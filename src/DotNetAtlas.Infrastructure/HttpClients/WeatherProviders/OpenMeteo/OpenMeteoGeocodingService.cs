using System.Net.Http.Json;
using DotNetAtlas.Application.WeatherForecast.Services.Abstractions;
using DotNetAtlas.Application.WeatherForecast.Services.Models;
using DotNetAtlas.Application.WeatherForecast.Services.Requests;
using DotNetAtlas.Domain.Entities.Weather.Forecast.Errors;
using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.Infrastructure.HttpClients.WeatherProviders.OpenMeteo;

public sealed class OpenMeteoGeocodingService : IGeocodingService
{
    public const string ServiceKey = "open-meteo-geo-service";
    public const string GeoHttpClientName = "open-meteo-geo";
    private readonly ILogger<OpenMeteoGeocodingService> _logger;
    private readonly HttpClient _geoHttpClient;

    public OpenMeteoGeocodingService(
        [FromKeyedServices(GeoHttpClientName)] HttpClient geoHttpClient,
        ILogger<OpenMeteoGeocodingService> logger)
    {
        _geoHttpClient = geoHttpClient;
        _logger = logger;
    }

    public async Task<Result<GeoCoordinates>> GetCoordinatesAsync(GeocodingRequest request, CancellationToken ct)
    {
        var countryCode = request.CountryCode.ToString().ToUpperInvariant();

        var geoResponse = await _geoHttpClient.GetFromJsonAsync<OpenMeteoGeoResponse>(
            $"v1/search" +
            $"?name={Uri.EscapeDataString(request.City)}" +
            $"&countryCode={Uri.EscapeDataString(countryCode)}" +
            $"&count=1" +
            $"&language=en" +
            $"&format=json", ct);

        var geoLocation = geoResponse?.Results?.FirstOrDefault();
        if (geoLocation is null)
        {
            _logger.LogInformation("Couldn't resolve location by: {City},{CountryCode}", request.City, request.CountryCode);

            return Result.Fail(ForecastErrors.CityNotFoundError(request.City, request.CountryCode));
        }

        _logger.LogDebug("Resolved location: {@GeoLocation} by: {City},{Code}", geoLocation, request.City, countryCode);

        return new GeoCoordinates(geoLocation.Latitude, geoLocation.Longitude);
    }
}
