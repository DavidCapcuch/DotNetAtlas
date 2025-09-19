using FastEndpoints;
using FluentValidation.Results;
using ZiggyCreatures.Caching.Fusion;

namespace DotNetAtlas.Api.Endpoints.Admin;

public class RemoveCacheByTagEndpoint : Endpoint<RemoveCacheByTagRequest>
{
    private readonly IFusionCache _fusionCache;
    private readonly ILogger<RemoveCacheByTagEndpoint> _logger;

    public RemoveCacheByTagEndpoint(
        IFusionCache fusionCache,
        ILogger<RemoveCacheByTagEndpoint> logger)
    {
        _fusionCache = fusionCache;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("clear-cache-by-tag/{Tag}");
        Version(1);
        Group<AdminGroup>();
        Summary(s =>
        {
            s.Summary = "Removes cache entries by tag.";
            s.ExampleRequest = new RemoveCacheByTagRequest
            {
                Tag = "Cz"
            };
        });
    }

    public override async Task HandleAsync(RemoveCacheByTagRequest request, CancellationToken ct)
    {
        _logger.LogInformation(
            "User {User} requested cache entries removal by {Tag}",
            User.Identity?.Name,
            request.Tag);

        if (string.IsNullOrWhiteSpace(request.Tag))
        {
            ValidationFailures.Add(new ValidationFailure(nameof(request.Tag), "Tag cannot be empty"));
            await Send.ErrorsAsync(422, ct);
            return;
        }

        await _fusionCache.RemoveByTagAsync(request.Tag, token: ct);

        _logger.LogInformation("Cache entries with tag {Tag} deleted", request.Tag);
        await Send.NoContentAsync(ct);
    }
}
