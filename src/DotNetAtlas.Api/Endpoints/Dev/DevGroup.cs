using DotNetAtlas.Api.Common.Authentication;
using FastEndpoints;

namespace DotNetAtlas.Api.Endpoints.Dev;

internal sealed class DevGroup : Group
{
    /// <summary>
    /// Hidden in production, only accessible in local/development environments.
    /// </summary>
    public DevGroup()
    {
        Configure(EndpointGroupConstants.Dev, ep =>
            {
                ep.Description(builder => builder
                    .WithGroupName(EndpointGroupConstants.Dev));
                ep.Tags(EndpointGroupConstants.Dev);
                ep.Policies(AuthPolicies.DevOnly);
            });
    }
}
