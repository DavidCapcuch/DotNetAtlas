namespace DotNetAtlas.Application.Forecast.GetForecasts;

public class GetForecastResponse
{
    public required IReadOnlyList<ForecastDto> Forecasts { get; set; }
}

public class ForecastDto
{
    public required DateOnly Date { get; set; }
    public required double MaxTemperatureC { get; set; }
    public required double MinTemperatureC { get; set; }
    public required string? Summary { get; set; }
}
