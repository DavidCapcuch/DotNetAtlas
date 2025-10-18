using System.Text.Json.Serialization;

namespace DotNetAtlas.Infrastructure.HttpClients.Weather.WeatherApiCom;

public sealed record WeatherApiForecastResponse
{
    public required ForecastSection? Forecast { get; set; }
}

public sealed record ForecastSection
{
    public required ForecastDay[]? Forecastday { get; set; }
}

public sealed record ForecastDay
{
    public string Date { get; set; } = string.Empty;
    public DaySection Day { get; set; } = new();
}

public sealed record DaySection
{
    [JsonPropertyName("avgtemp_c")]
    public double AvgTempC { get; set; }

    [JsonPropertyName("maxtemp_c")]
    public double MaxTempC { get; set; }

    [JsonPropertyName("mintemp_c")]
    public double MinTempC { get; set; }

    public ConditionSection? Condition { get; set; }
}

public sealed record ConditionSection
{
    public string? Text { get; set; }
}
