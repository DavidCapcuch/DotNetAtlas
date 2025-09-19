using FastEndpoints;
using ZiggyCreatures.Caching.Fusion;

namespace DotNetAtlas.Api.Endpoints.Admin;

public class ClearAllCacheEndpoint : Endpoint<ClearAllCacheRequest>
{
    private readonly IFusionCache _fusionCache;
    private readonly ILogger<ClearAllCacheEndpoint> _logger;

    public ClearAllCacheEndpoint(IFusionCache fusionCache, ILogger<ClearAllCacheEndpoint> logger)
    {
        _fusionCache = fusionCache;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("clear-all-cache");
        Version(1);
        Group<AdminGroup>();
        Summary(s =>
        {
            s.Summary = "Clears all cache entries.";
            s.Description =
                "Purges the entire cache store. Use with caution in production environments.<br/><br/>" +
                "AllowFailsafe - True: logical expire only, keep for failsafe. False: remove entries for good.";
            s.ExampleRequest = new ClearAllCacheRequest
            {
                AllowFailsafe = false
            };
        });
    }

    public override async Task HandleAsync(ClearAllCacheRequest request, CancellationToken ct)
    {
        _logger.LogInformation(
            "User {User} requested to clear all cache, AllowFailsafe: {AllowFailsafe}",
            User.Identity?.Name, request.AllowFailsafe);

        await _fusionCache.ClearAsync(request.AllowFailsafe, token: ct);

        _logger.LogInformation("All cache cleared");

        await Send.NoContentAsync(ct);
    }
}
