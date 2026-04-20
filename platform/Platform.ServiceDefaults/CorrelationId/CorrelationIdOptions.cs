namespace Platform.ServiceDefaults.CorrelationId;

/// <summary>
/// Configuration for the correlation-id HTTP edge (ADR-0008).
/// </summary>
public sealed class CorrelationIdOptions
{
    /// <summary>
    /// Configuration binding section.
    /// </summary>
    public const string Section = "CorrelationId";

    /// <summary>
    /// HTTP header name read on inbound requests and written on outbound requests + responses.
    /// Defaults to <see cref="CorrelationIdContextKeys.HttpHeaderName"/>.
    /// </summary>
    public string HeaderName { get; set; } = CorrelationIdContextKeys.HttpHeaderName;

    /// <summary>
    /// When <c>true</c>, missing / malformed inbound correlation ids are replaced by a freshly generated UUID v7.
    /// When <c>false</c>, a missing id leaves the ambient value unset and downstream consumers must cope.
    /// Per ADR-0008 the edge always accepts inbound requests; a false setting is primarily useful in internal
    /// services behind an edge gateway that already generates.
    /// </summary>
    public bool GenerateWhenMissing { get; set; } = true;
}
