using System.Net.Http.Json;
using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Weather.Domain.Alerts.Entities;
using Weather.Domain.Common.Services;
using Weather.Domain.Common.ValueObjects;
using Weather.Domain.Forecast.Errors;

namespace Weather.Infrastructure.HttpClients.WeatherProviders.WeatherApiCom;

public sealed class WeatherApiComGeocodingProvider : IGeocodingProvider
{
    public const string ServiceKey = "weatherapi-com-geo-service";
    private readonly ILogger<WeatherApiComGeocodingProvider> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public WeatherApiComGeocodingProvider(
        [FromKeyedServices(WeatherApiComProvider.HttpClientName)]
        HttpClient httpClient,
        IOptions<WeatherApiComOptions> options,
        ILogger<WeatherApiComGeocodingProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiKey = options.Value.ApiKey;
    }

    public async Task<Result<GeoCoordinates>> GetCoordinatesAsync(Location location, CancellationToken ct)
    {
        return await GetCoordinatesAsync(location.City, location.CountryCode, ct);
    }

    public async Task<Result<GeoCoordinates>> GetCoordinatesAsync(
        City city,
        CountryCode countryCode,
        CancellationToken ct)
    {
        var geoLocation = await GetGeoLocationAsync(city, countryCode, ct);
        if (geoLocation is null)
        {
            var cityWithCountry = $"{city},{countryCode}";
            _logger.LogInformation("Couldn't resolve location by: {CityWithCountry}", cityWithCountry);
            return Result.Fail(ForecastErrors.CityNotFoundError(city.Name, countryCode));
        }

        _logger.LogDebug("Resolved location: {@GeoLocation} by: {City},{CountryCode}", geoLocation, city, countryCode);
        return GeoCoordinates.Create(geoLocation.Lat, geoLocation.Lon);
    }

    public async Task<Result> ValidateLocationAsync(City city, CountryCode countryCode, CancellationToken ct)
    {
        var geoLocation = await GetGeoLocationAsync(city, countryCode, ct);
        if (geoLocation is null)
        {
            var cityWithCountry = $"{city},{countryCode}";
            _logger.LogInformation("Location validation failed for: {CityWithCountry}", cityWithCountry);
            return Result.Fail(ForecastErrors.CityNotFoundError(city.Name, countryCode));
        }

        _logger.LogDebug("Location validated: {City},{CountryCode}", city, countryCode);
        return Result.Ok();
    }

    private async Task<WeatherApiComGeo?> GetGeoLocationAsync(City city, CountryCode countryCode, CancellationToken ct)
    {
        var countryCodeString = countryCode.ToString().ToUpperInvariant();
        var cityWithCountry = $"{city},{countryCodeString}";
        var queryString = $"v1/search.json" +
                          $"?key={_apiKey}" +
                          $"&q={Uri.EscapeDataString(cityWithCountry)}";

        // WeatherAPI.com search endpoint returns an array directly, not a wrapper object
        var geoResponse = await _httpClient.GetFromJsonAsync<WeatherApiComGeo[]>(queryString, ct);
        return geoResponse?.FirstOrDefault();
    }
}
