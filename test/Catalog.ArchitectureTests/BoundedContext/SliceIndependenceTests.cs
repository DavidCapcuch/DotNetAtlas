using NetArchTest.Rules;

namespace Catalog.ArchitectureTests.BoundedContext;

/// <summary>
/// Per eshop-master-design.md § 11.4, each BC keeps its vertical slices independent: no slice
/// references a sibling slice. Cross-slice sharing goes through the two sanctioned sinks —
/// <c>Common</c> (shared read models, contracts, services) and <c>Domain</c> — never a direct
/// feature-to-feature reference. Response envelopes and their endpoint-specific item types are the
/// exception: ADR-0037 gives each endpoint its own, so those are copied into the referencing slice
/// rather than moved to a sink. Value DTOs that pass ADR-0037's knowledge test stay shared. This is
/// the intra-BC counterpart to the cross-BC reference tests.
/// </summary>
/// <remarks>
/// Generic by construction — slices are discovered by reflection over
/// <see cref="BaseTest.ApplicationAssembly"/>, so this file is copied per BC; only the namespace
/// and the allow-list may differ. A slice is a depth-2 namespace <c>{Root}.{Area}.{Feature}</c>;
/// <c>Common</c> at either the area or the feature position is excluded as a shared sink.
/// NetArchTest's built-in <c>Slice().ByNamespacePrefix()</c> slices at a single (area-coarse) level
/// and can't express depth-2 feature slices with cross-area detection + <c>Common</c> exclusion, so
/// the discovery is manual.
/// </remarks>
public class SliceIndependenceTests : BaseTest
{
    /// <summary>
    /// Sanctioned sibling-slice references, justified inline rather than by loosening the rule.
    /// Each entry allows types in <c>From</c> to depend on slice <c>To</c>. Empty by default, and
    /// meant to stay so: a slice shares through a sanctioned sink, never through a sibling.
    /// </summary>
    private static readonly (string From, string To)[] AllowedSliceCouplings = [];

    [Fact]
    public void Slices_ShouldNot_ReferenceSiblingSlices()
    {
        var root = ApplicationAssembly.GetName().Name!;

        var slices = DiscoverSlices(root);

        if (slices.Count < 2)
        {
            // Nothing to compare — a BC with 0–1 feature slices passes vacuously by design.
            return;
        }

        // Prefix guard: NetArchTest matches namespaces by StartsWith, so if one slice key is a raw
        // string-prefix of a sibling's (e.g. {Area}.Search next to {Area}.SearchAll), both
        // ResideInNamespace and HaveDependencyOnAny would over-match and silently mis-report. Fail
        // loudly and name the collision instead.
        var prefixCollisions = (
            from a in slices
            from b in slices
            where a != b && b.StartsWith(a, StringComparison.Ordinal)
            select $"'{a}' is a string-prefix of sibling '{b}'").ToList();

        prefixCollisions.Should().BeEmpty(
            "NetArchTest matches namespaces by StartsWith — a slice namespace that prefixes a " +
            "sibling produces false dependency results. Rename one feature so no slice namespace " +
            "prefixes another: " + string.Join(", ", prefixCollisions));

        var allFailingTypes = new List<string>();

        foreach (var slice in slices)
        {
            var otherSlices = slices
                .Where(other => other != slice)
                .Where(other => !AllowedSliceCouplings.Contains((slice, other)))
                .ToArray();

            if (otherSlices.Length == 0)
            {
                continue;
            }

            var result = Types.InAssembly(ApplicationAssembly)
                .That()
                .ResideInNamespace(slice)
                .ShouldNot()
                .HaveDependencyOnAny(otherSlices)
                .GetResult();

            // Cross-area coupling counts: {AreaA}.X -> {AreaB}.Y is a sibling-slice reference
            // just as much as a same-area one — the correct reading of "no slice references a
            // sibling slice".
            allFailingTypes.AddRange(result.FailingTypes.Select(t => $"{t.FullName} (in {slice})"));
        }

        allFailingTypes.Should().BeEmpty(
            "No vertical slice may reference a sibling slice. Where the referenced type is shared " +
            "knowledge — a value DTO, a projection row — move it into Common/Domain. Where it is a " +
            "published wire contract — a response envelope or its endpoint-specific item type — " +
            "ADR-0037 requires each endpoint to own one, so copy it into this slice instead of " +
            "sharing it. Types holding a sibling dependency: " + string.Join(", ", allFailingTypes));
    }

    private static List<string> DiscoverSlices(string root)
    {
        var prefix = root + ".";
        var slices = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in ApplicationAssembly.GetTypes())
        {
            var ns = type.Namespace;
            if (ns is null || !ns.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var segments = ns[prefix.Length..].Split('.');
            if (segments.Length < 2)
            {
                continue;
            }

            if (segments[0] == "Common" || segments[1] == "Common")
            {
                continue;
            }

            slices.Add($"{root}.{segments[0]}.{segments[1]}");
        }

        return slices.OrderBy(s => s, StringComparer.Ordinal).ToList();
    }
}
