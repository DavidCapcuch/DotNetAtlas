using System.Net.Http.Json;
using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Weather.Domain.Alerts.Entities;
using Weather.Domain.Common.Services;
using Weather.Domain.Common.ValueObjects;
using Weather.Domain.Forecast.Errors;

namespace Weather.Infrastructure.HttpClients.WeatherProviders.OpenMeteo;

public sealed class OpenMeteoGeocodingProvider : IGeocodingProvider
{
    public const string ServiceKey = "open-meteo-geo-service";
    public const string GeoHttpClientName = "open-meteo-geo";
    private readonly ILogger<OpenMeteoGeocodingProvider> _logger;
    private readonly HttpClient _geoHttpClient;

    public OpenMeteoGeocodingProvider(
        [FromKeyedServices(GeoHttpClientName)] HttpClient geoHttpClient,
        ILogger<OpenMeteoGeocodingProvider> logger)
    {
        _geoHttpClient = geoHttpClient;
        _logger = logger;
    }

    public async Task<Result<GeoCoordinates>> GetCoordinatesAsync(Location location, CancellationToken ct)
    {
        return await GetCoordinatesAsync(location.City, location.CountryCode, ct);
    }

    public async Task<Result<GeoCoordinates>> GetCoordinatesAsync(City city,
        CountryCode countryCode,
        CancellationToken ct)
    {
        var geoLocation = await GetGeoLocationAsync(city, countryCode, ct);
        if (geoLocation is null)
        {
            _logger.LogInformation("Couldn't resolve location by: {City},{CountryCode}", city,
                countryCode);
            return Result.Fail(ForecastErrors.CityNotFoundError(city.Name, countryCode));
        }

        _logger.LogDebug("Resolved location: {@GeoLocation} by: {City},{Code}", geoLocation, city, countryCode);
        return GeoCoordinates.Create(geoLocation.Latitude, geoLocation.Longitude);
    }

    public async Task<Result> ValidateLocationAsync(City city, CountryCode countryCode, CancellationToken ct)
    {
        var geoLocation = await GetGeoLocationAsync(city, countryCode, ct);
        if (geoLocation is null)
        {
            _logger.LogInformation("Location validation failed for: {City},{CountryCode}", city, countryCode);
            return Result.Fail(ForecastErrors.CityNotFoundError(city.Name, countryCode));
        }

        _logger.LogDebug("Location validated: {City},{CountryCode}", city, countryCode);
        return Result.Ok();
    }

    private async Task<OpenMeteoGeo?> GetGeoLocationAsync(City city, CountryCode countryCode, CancellationToken ct)
    {
        var countryCodeString = countryCode.ToString().ToUpperInvariant();

        var geoResponse = await _geoHttpClient.GetFromJsonAsync<OpenMeteoGeoResponse>(
            $"v1/search" +
            $"?name={Uri.EscapeDataString(city.Name)}" +
            $"&countryCode={Uri.EscapeDataString(countryCodeString)}" +
            $"&count=1" +
            $"&language=en" +
            $"&format=json", ct);

        return geoResponse?.Results?.FirstOrDefault();
    }
}
