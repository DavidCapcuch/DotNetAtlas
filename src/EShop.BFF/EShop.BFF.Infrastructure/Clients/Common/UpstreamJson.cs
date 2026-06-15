using System.Text.Json;

namespace EShop.BFF.Infrastructure.Clients.Common;

/// <summary>Shared JSON options for upstream HTTP deserialization — web defaults (camelCase,
/// case-insensitive), matching the services' FastEndpoints wire shape.</summary>
internal static class UpstreamJson
{
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
}
