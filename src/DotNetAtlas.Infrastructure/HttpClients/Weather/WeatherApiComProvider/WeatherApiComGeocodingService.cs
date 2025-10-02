using System.Net.Http.Json;
using DotNetAtlas.Application.Forecast.Services.Abstractions;
using DotNetAtlas.Application.Forecast.Services.Models;
using DotNetAtlas.Application.Forecast.Services.Requests;
using DotNetAtlas.Domain.Errors;
using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Infrastructure.HttpClients.Weather.WeatherApiComProvider;

public sealed class WeatherApiComGeocodingService : IGeocodingService
{
    public const string ServiceKey = "weatherapi-com-geo-service";
    private readonly ILogger<WeatherApiComGeocodingService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public WeatherApiComGeocodingService(
        [FromKeyedServices(WeatherApiComProvider.HttpClientName)]
        HttpClient httpClient,
        IOptions<WeatherApiComOptions> options,
        ILogger<WeatherApiComGeocodingService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiKey = options.Value.ApiKey;
    }

    public async Task<Result<GeoCoordinates>> GetCoordinatesAsync(GeocodingRequest request, CancellationToken ct)
    {
        var countryCode = request.CountryCode.ToString().ToUpperInvariant();
        var cityWithCountry = $"{request.City},{countryCode}";
        var query = $"v1/search.json" +
                    $"?key={_apiKey}" +
                    $"&q={Uri.EscapeDataString(cityWithCountry)}";

        var geoResponse = await _httpClient.GetFromJsonAsync<List<LocationSearchItem>>(query, ct);

        var geoLocation = geoResponse?.FirstOrDefault();
        if (geoLocation is null)
        {
            _logger.LogInformation("Couldn't resolve location by: {CityWithCountry}", cityWithCountry);

            return Result.Fail(WeatherForecastErrors.CityNotFoundError(request.City, request.CountryCode));
        }

        _logger.LogDebug("Resolved location: {@GeoLocation} by: {CityWithCountry}", geoLocation, cityWithCountry);

        return new GeoCoordinates(geoLocation.Lat, geoLocation.Lon);
    }

    private sealed class LocationSearchItem
    {
        public double Lat { get; set; }
        public double Lon { get; set; }
    }
}
