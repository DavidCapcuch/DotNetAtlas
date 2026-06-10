namespace Notifications.Api.Common.Constants;

/// <summary>
/// Reserved request-path prefixes for the Notifications API. <see cref="HubsBasePath"/> scopes the
/// SignalR query-string auth lift and prefixes the versioned hub route.
/// </summary>
internal static class BasePaths
{
    public const string HubsBasePath = "/hubs";
}
