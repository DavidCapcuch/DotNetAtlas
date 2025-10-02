using DotNetAtlas.Application.Common.CQS;
using DotNetAtlas.Domain.Entities.Weather.Forecast;

namespace DotNetAtlas.Application.WeatherAlerts.SendWeatherAlert;

public class SendWeatherAlertCommand : ICommand
{
    public required string City { get; set; }
    public required CountryCode CountryCode { get; set; }
    public required string Message { get; set; }
}
