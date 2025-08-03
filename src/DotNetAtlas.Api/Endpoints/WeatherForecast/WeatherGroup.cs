using FastEndpoints;

namespace DotNetAtlas.Api.Endpoints.WeatherForecast
{
    public sealed class WeatherGroup : Group
    {
        public WeatherGroup()
        {
            Configure("/weather", ep =>
            {
                ep.Description(builder => builder
                    .WithGroupName(EndpointGroupConstants.WEATHER));
                ep.Tags(EndpointGroupConstants.WEATHER);
                ep.AllowAnonymous();
            });
        }
    }
}