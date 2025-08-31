using FastEndpoints;

namespace DotNetAtlas.Api.Endpoints.Weather;

internal sealed class WeatherGroup : Group
{
    public WeatherGroup()
    {
        Configure("/weather", ep =>
        {
            ep.Description(builder => builder
                .WithGroupName(EndpointGroupConstants.Weather));
            ep.Tags(EndpointGroupConstants.Weather);
        });
    }
}
