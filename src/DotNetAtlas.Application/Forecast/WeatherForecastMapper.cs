using DotNetAtlas.Application.Forecast.GetForecasts;
using DotNetAtlas.Application.Forecast.Services.Requests;
using Riok.Mapperly.Abstractions;

namespace DotNetAtlas.Application.Forecast;

[Mapper]
public static partial class WeatherForecastMapper
{
    [MapperRequiredMapping(RequiredMappingStrategy.Target)]
    public static partial ForecastRequest ToForecastRequest(this GetForecastQuery getForecastQuery);

    [MapperRequiredMapping(RequiredMappingStrategy.Target)]
    public static partial GeocodingRequest ToGeocodingRequest(this ForecastRequest forecastRequest);
}
