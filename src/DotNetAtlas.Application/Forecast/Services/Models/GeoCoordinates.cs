namespace DotNetAtlas.Application.Forecast.Services.Models;

public sealed record GeoCoordinates
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
}
