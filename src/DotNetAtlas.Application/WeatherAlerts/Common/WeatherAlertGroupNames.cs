using DotNetAtlas.Application.WeatherAlerts.Common.Contracts;

namespace DotNetAtlas.Application.WeatherAlerts.Common;

public static class WeatherAlertGroupNames
{
    public static string GroupByCitySubscriptionRequest(AlertSubscriptionDto dto) =>
        $"{dto.City.ToUpperInvariant()}:{dto.CountryCode.ToString().ToUpperInvariant()}";
}
