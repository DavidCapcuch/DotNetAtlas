namespace DotNetAtlas.Infrastructure.HttpClients.Weather.OpenMeteoProvider;

public sealed record OpenMeteoGeoResponse
{
    public OpenMeteoGeo[]? Results { get; set; }
}

public sealed record OpenMeteoGeo
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}
