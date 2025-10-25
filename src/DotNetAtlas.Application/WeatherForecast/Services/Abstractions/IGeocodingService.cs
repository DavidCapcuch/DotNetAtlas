using DotNetAtlas.Application.WeatherForecast.Services.Models;
using DotNetAtlas.Application.WeatherForecast.Services.Requests;
using FluentResults;

namespace DotNetAtlas.Application.WeatherForecast.Services.Abstractions;

public interface IGeocodingService
{
    Task<Result<GeoCoordinates>> GetCoordinatesAsync(GeocodingRequest request, CancellationToken ct);
}
