namespace Weather.Api.Endpoints.Admin;

public sealed class ClearAllCacheRequest
{
    public required bool AllowFailsafe { get; init; }
}
