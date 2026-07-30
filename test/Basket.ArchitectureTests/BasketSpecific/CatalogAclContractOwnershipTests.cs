using System.Collections.Frozen;
using System.Reflection;

namespace Basket.ArchitectureTests.BasketSpecific;

/// <summary>
/// Each Catalog route owns the anti-corruption record Basket binds its response to (basket.md
/// &#xa7; 9.3). One record bound to two routes silently asserts that two independently-evolving
/// upstream contracts emit the same shape — the assumption
/// <see href="https://github.com/DavidCapcuch/DotNetAtlas/blob/main/docs/adr/0037-endpoint-owned-response-contracts.md">ADR-0037</see>
/// says no service guarantees, since it leaves sibling endpoints free to diverge.
/// </summary>
/// <remarks>
/// <para>
/// Binding is strict, so a record shared across two routes also widens what each route must keep
/// emitting: a member only one call site reads becomes a member neither route may drop. Splitting is
/// what keeps each route's required set down to what that route's caller actually reads.
/// </para>
/// <para>
/// Route roots are derived structurally — an ACL type nothing else in the ACL references is one a
/// route binds directly. The BFF gets the same property from its client interfaces'
/// <c>Task&lt;Result&lt;T&gt;&gt;</c> signatures; that discovery finds nothing here, because Basket
/// has a real ACL where the BFF has none: its port returns the domain VO and binding happens inside
/// private adapter methods. Reading the <c>ReadFromJsonAsync&lt;T&gt;</c> call sites via Mono.Cecil
/// (which <see cref="BaseTest"/> already does elsewhere) would also work, but trades a structural
/// property for a call-name convention.
/// </para>
/// <para>
/// Four self-checks keep the rule from quietly guarding nothing: at least two roots, the
/// collection-nested item type actually reached, every exemption resolving to a real type, and
/// closure — every ACL type is a root, named machinery, or reached from a root.
/// </para>
/// <para>
/// Types are named as strings because <c>Basket.Infrastructure</c> does not grant
/// <c>InternalsVisibleTo</c> to this project, and widening that to satisfy a test would be the wrong
/// trade. The exemption stays licensed <em>by name</em> either way, which is what ADR-0037 requires —
/// the namespace below scopes only what the rule examines, never what it exempts.
/// </para>
/// </remarks>
public class CatalogAclContractOwnershipTests : BaseTest
{
    private const string AclNamespace = "Basket.Infrastructure.ExternalServices.Catalog";

    /// <summary>A type reached only through a collection, so a walk that stopped at the top-level
    /// record fails here instead of passing silently over every nested record.</summary>
    private const string CollectionNestedAclType = AclNamespace + ".CatalogProductsByIdsItem";

    /// <summary>The ACL types that are machinery rather than wire shape, and so are neither route
    /// roots nor reachable from one.</summary>
    private static readonly FrozenSet<string> AclMachineryNames = new[]
    {
        AclNamespace + ".ProductCatalogHttpAdapter",
        AclNamespace + ".CatalogClientDependencyInjection",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Types deliberately reachable from more than one route, each because a change at one binding
    /// site would necessarily be the same change at the others (the knowledge test — ADR-0037
    /// &#xa7; Rationale).
    /// </summary>
    private static readonly FrozenSet<string> SharedAclTypes = new[]
    {
        // Amount + currency representation is a service-wide decision, not a route's — the inbound
        // mirror of ADR-0037's MoneyDto ruling.
        AclNamespace + ".CatalogPriceDto",
    }.ToFrozenSet(StringComparer.Ordinal);

    [Fact]
    public void EveryCatalogAclRecord_IsReachableFrom_AtMostOneRoute()
    {
        var roots = DiscoverRouteResponses();

        roots.Should().HaveCountGreaterThanOrEqualTo(
            2,
            "the rule compares routes against each other, so it means nothing with fewer than two. " +
            "Fewer found means the ACL's records collapsed into one graph — which is what merging " +
            "two routes' records looks like from here. Discovered: {0}",
            string.Join(", ", roots.Select(root => root.FullName)));

        var reachedBy = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var root in roots)
        {
            foreach (var reached in ReachableAclTypes(root))
            {
                if (!reachedBy.TryGetValue(reached, out var reachedFromRoots))
                {
                    reachedFromRoots = new HashSet<string>(StringComparer.Ordinal);
                    reachedBy[reached] = reachedFromRoots;
                }

                reachedFromRoots.Add(root.FullName!);
            }
        }

        var overShared = reachedBy
            .Where(entry => entry.Value.Count > 1 && !SharedAclTypes.Contains(entry.Key))
            .Select(entry => $"{entry.Key} ({string.Join(", ", entry.Value.Order(StringComparer.Ordinal))})")
            .Order(StringComparer.Ordinal)
            .ToList();

        overShared.Should().BeEmpty(
            "each Catalog route owns the record Basket binds it to, carrying only what that route's " +
            "caller reads (basket.md § 9.3). The fix is to copy the record for the second route and " +
            "trim it to what that caller reads — not to relocate it. Share it only if a change at " +
            "one binding site would always be the same change at the other, and then name it in " +
            "{0}. Reachable from several routes: {1}",
            nameof(SharedAclTypes),
            string.Join("; ", overShared));

        reachedBy.Should().ContainKey(
            CollectionNestedAclType,
            "the walk must reach past a route's top-level record into its collection item types — if " +
            "it does not, the rule silently passes over every nested record, which is exactly the " +
            "shape of the sharing it exists to catch");

        SharedAclTypes.Where(name => !reachedBy.ContainsKey(name)).Should().BeEmpty(
            "an exemption naming a type no route reaches is a stale exemption — it means the type " +
            "was renamed or removed and the allow-list silently kept licensing a name that no longer " +
            "exists");

        var orphans = AclTypes()
            .Select(type => type.FullName!)
            .Where(name => !reachedBy.ContainsKey(name) && !AclMachineryNames.Contains(name))
            .Order(StringComparer.Ordinal)
            .ToList();

        orphans.Should().BeEmpty(
            "closure: every ACL type must be a route root, named machinery, or reached from a root. " +
            "A type outside all three is a record no route was compared against, so its sharing was " +
            "never checked — the way a third route silently leaves this rule's subject. Add it to a " +
            "route's graph, or name it in {0}: {1}",
            nameof(AclMachineryNames),
            string.Join(", ", orphans));
    }

    /// <summary>
    /// Route roots: the ACL records Basket binds a Catalog response to, one per route. See the
    /// remarks on why this is a naming convention rather than a read of the binding call sites.
    /// </summary>
    /// <summary>
    /// The ACL's wire types — everything under the ACL namespace that is not the adapter or its DI
    /// extension. Matched by namespace <em>prefix</em>, so relocating a shared record into a
    /// <c>…Catalog.Shared</c> sub-namespace cannot hide it from the walk. Relocation is the tempting
    /// wrong fix this rule's failure message warns against, and prose is not a constraint.
    /// </summary>
    private static IEnumerable<Type> AclTypes()
        => InfrastructureAssembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith(AclNamespace, StringComparison.Ordinal) == true)
            .Where(type => !type.IsNested);

    /// <summary>
    /// Route roots: an ACL type no other ACL type references is one a route binds directly. Derived
    /// structurally rather than by name, so a route record added later is compared whatever it is
    /// called — see the remarks for why the BFF's signature-based discovery does not port here.
    /// </summary>
    private static List<Type> DiscoverRouteResponses()
    {
        var acl = AclTypes().Where(type => !AclMachineryNames.Contains(type.FullName!)).ToList();

        var referenced = acl
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .SelectMany(property => AclTypesIn(property.PropertyType))
            .Select(type => type.FullName!)
            .ToHashSet(StringComparer.Ordinal);

        return acl
            .Where(type => !referenced.Contains(type.FullName!))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();
    }

    private static HashSet<string> ReachableAclTypes(Type root)
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<Type>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!reached.Add(current.FullName!))
            {
                continue;
            }

            foreach (var property in current.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (var owned in AclTypesIn(property.PropertyType))
                {
                    pending.Push(owned);
                }
            }
        }

        return reached;
    }

    /// <summary>
    /// Unwraps generic arguments so a record nested as <c>IReadOnlyList&lt;T&gt;</c> counts as
    /// reached — it is exactly as shared as one declared directly, it just does not show up in a
    /// grep of binding call sites. Bounding on the ACL namespace keeps <see cref="Guid"/>,
    /// <see cref="string"/> and the collection types themselves out of the rule's subject.
    /// </summary>
    private static IEnumerable<Type> AclTypesIn(Type type)
    {
        if (type.Namespace?.StartsWith(AclNamespace, StringComparison.Ordinal) == true)
        {
            yield return type;
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                foreach (var owned in AclTypesIn(argument))
                {
                    yield return owned;
                }
            }
        }
    }
}
