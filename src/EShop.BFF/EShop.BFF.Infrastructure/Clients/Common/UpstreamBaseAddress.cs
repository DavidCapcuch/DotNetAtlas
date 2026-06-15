namespace EShop.BFF.Infrastructure.Clients.Common;

/// <summary>Normalizes a configured base URL into an absolute <see cref="Uri"/> with a trailing
/// slash, so clients can issue relative request paths (e.g. <c>api/v1/catalog/...</c>) that combine
/// correctly.</summary>
internal static class UpstreamBaseAddress
{
    public static Uri From(string baseUrl)
    {
        var withTrailingSlash = baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
        return new Uri(withTrailingSlash, UriKind.Absolute);
    }
}
