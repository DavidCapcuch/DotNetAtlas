using DotNetAtlas.Application.Forecast.Services.Models;
using DotNetAtlas.Application.Forecast.Services.Requests;
using FluentResults;

namespace DotNetAtlas.Application.Forecast.Services.Abstractions;

public interface IGeocodingService
{
    Task<Result<GeoCoordinates>> GetCoordinatesAsync(GeocodingRequest request, CancellationToken ct);
}
