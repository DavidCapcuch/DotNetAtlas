namespace Weather.Infrastructure.HttpClients.WeatherProviders.WeatherApiCom;

/// <summary>
/// WeatherAPI.com search.json returns an array directly, not a wrapper object.
/// This type alias makes the intent clear.
/// </summary>
public sealed record WeatherApiComGeo
{
    public double Lat { get; set; }
    public double Lon { get; set; }
}
