using DotNetAtlas.Application.Common.CQS;

namespace DotNetAtlas.Application.Forecast.GetForecasts;

public class GetForecastsQuery : IQuery<GetForecastsResponse>
{
    /// <summary>
    /// Number of days of forecast (1-14).
    /// </summary>
    public required int Days { get; set; }
}
