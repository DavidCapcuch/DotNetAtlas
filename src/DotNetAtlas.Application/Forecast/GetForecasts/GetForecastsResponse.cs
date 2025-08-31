namespace DotNetAtlas.Application.Forecast.GetForecasts;

public class GetForecastsResponse
{
    public required IAsyncEnumerable<ForecastResponse> Forecasts { get; set; }
}
