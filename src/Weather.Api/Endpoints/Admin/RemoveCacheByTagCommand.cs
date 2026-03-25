using FastEndpoints;

namespace Weather.Api.Endpoints.Admin;

public sealed class RemoveCacheByTagCommand
{
    [RouteParam]
    public required string Tag { get; init; }
}
