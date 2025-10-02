using DotNetAtlas.Application.Common.CQS;
using DotNetAtlas.Domain.Entities.Weather.Forecast;

namespace DotNetAtlas.Application.WeatherAlerts.UnsubscribeFromCityAlerts;

public class UnsubscribeFromCityAlertsCommand : ICommand
{
    public required string City { get; set; }
    public required CountryCode CountryCode { get; set; }
    public required string ConnectionId { get; set; }
}
