using System.Text.Json.Serialization;

namespace DotNetAtlas.Infrastructure.HttpClients.WeatherProviders.OpenMeteo;

public sealed record OpenMeteoForecastResponse
{
    public required DailySection? Daily { get; set; }
}

public sealed record DailySection
{
    public required string[] Time { get; set; }

    [JsonPropertyName("temperature_2m_max")]
    public required double[] TemperatureMax { get; set; }

    [JsonPropertyName("temperature_2m_min")]
    public required double[] TemperatureMin { get; set; }
}
