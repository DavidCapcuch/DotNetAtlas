using FastEndpoints;

namespace DotNetAtlas.Api.Endpoints.Admin;

public sealed class RemoveCacheByTagRequest
{
    [RouteParam]
    public required string Tag { get; init; }
}
