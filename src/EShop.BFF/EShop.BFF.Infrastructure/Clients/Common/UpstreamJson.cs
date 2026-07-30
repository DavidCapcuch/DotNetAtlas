using System.Text.Json;

namespace EShop.BFF.Infrastructure.Clients.Common;

/// <summary>
/// Shared JSON options for upstream HTTP deserialization — web defaults (camelCase, case-insensitive),
/// matching the services' FastEndpoints wire shape, plus strict binding (bff.md § 4.1).
/// </summary>
/// <remarks>
/// <para>
/// Strict binding is <b>asymmetric by design</b>: an upstream that <em>drops</em> or nulls a member the BFF
/// binds throws here, while one that <em>adds</em> a member binds unaffected. Removal is what breaks a page;
/// addition is not. So <c>UnmappedMemberHandling</c> stays at its default — disallowing unknown members
/// would turn every upstream addition into a BFF outage, the opposite of what this layer is for.
/// </para>
/// <para>
/// A throw lands as <c>JsonException</c> in each typed client's catch, so an upstream shape the BFF cannot
/// bind degrades exactly like an unreachable upstream: enrichment reads drop to partial/stale, gating reads
/// fail closed with 503. Without it a dropped member binds to <c>default</c> and surfaces as a
/// <c>NullReferenceException</c> during composition — a 500 for a condition already modelled as degradation.
/// </para>
/// <para>
/// The two settings close different holes.
/// <see cref="JsonSerializerOptions.RespectNullableAnnotations"/> rejects a member that is <em>present but
/// null</em>, which <c>required</c> alone does not — the member is present, so the requirement is satisfied.
/// <see cref="JsonSerializerOptions.RespectRequiredConstructorParameters"/> extends the absent-member check
/// to positional records, so the guard does not depend on every ACL record being declared property-style.
/// </para>
/// </remarks>
internal static class UpstreamJson
{
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };
}
