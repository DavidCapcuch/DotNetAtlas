using DotNetAtlas.Infrastructure.Common.Authorization;
using FastEndpoints;

namespace DotNetAtlas.Api.Endpoints.Admin;

public sealed class AdminGroup : Group
{
    public AdminGroup()
    {
        Configure(EndpointGroupConstants.Admin, ep =>
        {
            ep.Description(builder => builder
                .WithGroupName(EndpointGroupConstants.Admin));
            ep.Tags(EndpointGroupConstants.Admin);
            ep.Policies(AuthPolicies.DevOnly);
        });
    }
}
