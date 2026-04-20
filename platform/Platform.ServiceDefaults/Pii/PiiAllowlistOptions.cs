namespace Platform.ServiceDefaults.Pii;

/// <summary>
/// Options binding section for the OTel PII allowlist processor (ADR-0011).
/// Consumers extend — not replace — the hard-coded default allowlist by populating
/// <see cref="AdditionalAttributes"/> or <see cref="AdditionalPrefixes"/>.
/// </summary>
public sealed class PiiAllowlistOptions
{
    /// <summary>
    /// Configuration section name: <c>Observability:PiiAllowlist</c>.
    /// </summary>
    public const string Section = "Observability:PiiAllowlist";

    /// <summary>
    /// Additional exact-match allowlisted span-attribute keys (merged with the hard-coded defaults).
    /// </summary>
    public string[] AdditionalAttributes { get; init; } = [];

    /// <summary>
    /// Additional prefix-match allowlisted keys — any span-attribute whose key starts with one
    /// of these strings is kept (merged with the hard-coded default prefixes).
    /// </summary>
    public string[] AdditionalPrefixes { get; init; } = [];
}
