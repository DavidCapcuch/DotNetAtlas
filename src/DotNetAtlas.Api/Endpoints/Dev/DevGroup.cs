using DotNetAtlas.Api.Common.Authentication;
using FastEndpoints;

namespace DotNetAtlas.Api.Endpoints.Dev
{
    public sealed class DevGroup : Group
    {
        /// <summary>
        /// Hidden in production, only accessible in local/development environments.
        /// </summary>
        public DevGroup()
        {
            Configure(EndpointGroupConstants.DEV, ep =>
            {
                ep.Description(builder => builder
                    .WithGroupName(EndpointGroupConstants.DEV));
                ep.Tags(EndpointGroupConstants.DEV);
                ep.Policies(AuthPolicies.DEV_ONLY);
            });
        }
    }
}