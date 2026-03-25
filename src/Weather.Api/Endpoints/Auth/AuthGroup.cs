using FastEndpoints;

namespace Weather.Api.Endpoints.Auth;

public sealed class AuthGroup : Group
{
    public AuthGroup()
    {
        Configure(EndpointGroupConstants.Auth, ep =>
        {
            ep.Description(builder => builder
                .WithGroupName(EndpointGroupConstants.Auth)
                .ExcludeFromDescription());
            ep.Tags(EndpointGroupConstants.Auth);
        });
    }
}
