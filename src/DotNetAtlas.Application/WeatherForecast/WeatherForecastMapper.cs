using DotNetAtlas.Application.WeatherForecast.GetForecasts;
using DotNetAtlas.Application.WeatherForecast.Services.Requests;
using Riok.Mapperly.Abstractions;

namespace DotNetAtlas.Application.WeatherForecast;

[Mapper]
public static partial class WeatherForecastMapper
{
    [MapperRequiredMapping(RequiredMappingStrategy.Target)]
    public static partial ForecastRequest ToForecastRequest(this GetForecastQuery getForecastQuery);

    [MapperRequiredMapping(RequiredMappingStrategy.Target)]
    public static partial GeocodingRequest ToGeocodingRequest(this ForecastRequest forecastRequest);
}
