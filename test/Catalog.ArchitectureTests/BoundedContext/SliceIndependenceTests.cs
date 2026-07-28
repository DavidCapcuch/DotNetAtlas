using NetArchTest.Rules;

namespace Catalog.ArchitectureTests.BoundedContext;

/// <summary>
/// Per eshop-master-design.md § 11.4, each BC keeps its vertical slices independent: no slice
/// references a sibling slice. Cross-slice sharing goes through the two sanctioned sinks —
/// <c>Common</c> (shared read models, contracts, services) and <c>Domain</c> — never a direct
/// feature-to-feature reference. This is the intra-BC counterpart to
/// <see cref="CrossBcReferenceTests"/> (which guards the cross-BC boundary).
/// </summary>
/// <remarks>
/// Generic by construction — slices are discovered by reflection over
/// <see cref="BaseTest.ApplicationAssembly"/>, so this file is copied verbatim across every BC's
/// <c>ArchitectureTests</c> (only the namespace and, if ever needed, the allow-list change). A
/// slice is a depth-2 namespace <c>{Root}.{Area}.{Feature}</c> (e.g.
/// <c>Catalog.Application.Products.CreateProduct</c>); <c>Common</c> at either the area or the
/// feature position is excluded as a shared sink. NetArchTest's built-in
/// <c>Slice().ByNamespacePrefix()</c> slices at a single (area-coarse) level and can't express
/// depth-2 feature slices with cross-area detection + <c>Common</c> exclusion, so the discovery is
/// manual.
/// </remarks>
public class SliceIndependenceTests : BaseTest
{
    /// <summary>
    /// Sanctioned sibling-slice references, justified inline rather than by loosening the rule.
    /// Each entry allows types in <c>From</c> to depend on slice <c>To</c>. Empty by default, and
    /// meant to stay so: a slice shares through a sanctioned sink, never through a sibling.
    /// </summary>
    /// <remarks>
    /// Relocating a type into a sink is not on its own a fix. This rule and ADR-0037's
    /// one-endpoint-per-response-type rule are independent, and both must hold: a response
    /// <em>envelope</em> moved into <c>Common</c> satisfies this test while still coupling the
    /// endpoints that share it — green here, and still an ADR-0037 violation. Sinks are for types
    /// with one owner and many readers (value DTOs, projection rows), not for contracts whose
    /// sharing is the defect.
    /// </remarks>
    private static readonly (string From, string To)[] AllowedSliceCouplings = [];

    [Fact]
    public void Slices_ShouldNot_ReferenceSiblingSlices()
    {
        var root = ApplicationAssembly.GetName().Name!;

        var slices = DiscoverSlices(root);

        if (slices.Count < 2)
        {
            // Nothing to compare — a BC with 0–1 feature slices passes vacuously by design
            // (mirrors the < 2 precedent in AggregateRootTests).
            return;
        }

        // Prefix guard: NetArchTest matches namespaces by StartsWith, so if one slice key is a raw
        // string-prefix of a sibling's (e.g. a future 'Search' next to 'SearchProducts'), both
        // ResideInNamespace and HaveDependencyOnAny would over-match and silently mis-report. Fail
        // loudly and name the collision instead. No current collisions exist.
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

            // Cross-area coupling counts: Products.X -> Categories.Y is a sibling-slice reference
            // just as much as a same-area one — the correct reading of "no slice references a
            // sibling slice".
            allFailingTypes.AddRange(result.FailingTypes.Select(t => $"{t.FullName} (in {slice})"));
        }

        allFailingTypes.Should().BeEmpty(
            "No vertical slice may reference a sibling slice — share through Common/Domain instead. " +
            "Offending types: " + string.Join(", ", allFailingTypes));
    }

    /// <summary>
    /// Discovers the BC's vertical slices as the distinct depth-2 namespaces
    /// <c>{root}.{Area}.{Feature}</c>. A namespace shallower than depth-2 (the bare root or a
    /// <c>root+1</c> shared namespace) is not a slice; <c>Common</c> at either the area or the
    /// feature position (e.g. <c>{root}.Common.ReadModels</c> or <c>{root}.Categories.Common.Services</c>)
    /// is an excluded shared sink. Types deeper than depth-2 map to their depth-2 ancestor.
    /// </summary>
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
