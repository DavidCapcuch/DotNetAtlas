using DotNetAtlas.Application.Common.CQS;
using DotNetAtlas.Domain.Entities.Weather.Forecast;

namespace DotNetAtlas.Application.WeatherAlerts.SubscribeForCityAlerts;

public class SubscribeForCityAlertsCommand : ICommand
{
    public required string City { get; set; }
    public required CountryCode CountryCode { get; set; }
    public required string ConnectionId { get; set; }
}
