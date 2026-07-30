using System.Collections.Frozen;
using System.Reflection;
using EShop.BFF.Infrastructure.Clients.Basket;
using EShop.BFF.Infrastructure.Clients.Catalog;
using FluentResults;

namespace EShop.BFF.ArchitectureTests.ApiContracts;

/// <summary>
/// The consumer-side counterpart of <see cref="EndpointOwnedResponseContractTests"/>: each upstream
/// <em>route</em> owns the anti-corruption record the BFF binds its response to (bff.md § 4.1). One record
/// bound to two client methods silently asserts that two independently-evolving upstream contracts emit the
/// same shape — the assumption this rule exists to prevent.
/// </summary>
/// <remarks>
/// Binding is strict (<c>UpstreamJson.Web</c>), so a record shared across two routes also widens what each
/// route must keep emitting: a member only one page renders becomes a member both routes cannot drop.
/// Splitting is what keeps each route's required set to what that page actually reads.
/// </remarks>
public sealed class UpstreamContractOwnershipTests : BaseTest
{
    /// <summary>A type reached only through a collection, so a walk that stopped at the top-level response
    /// fails here instead of passing over unchecked records.</summary>
    private static readonly Type CollectionNestedUpstreamType = typeof(CatalogProductPricingDto);

    /// <summary>
    /// Types deliberately bound by more than one client method, each because a change to one binding site
    /// would necessarily be the same change at the others (the knowledge test — ADR-0037 § Rationale).
    /// </summary>
    private static readonly FrozenSet<Type> SharedUpstreamTypes = new[]
    {
        // Amount + currency representation is an API-wide decision, not a route's — the inbound mirror of
        // ADR-0037's MoneyDto ruling.
        typeof(CatalogMoneyDto),

        // Not an upstream shape at all: the BFF's own envelope for a verbatim relay (status + raw body).
        // The four basket mutation forwarders relay identically, so how a verdict is represented is one
        // decision for all four.
        typeof(BasketWriteVerdict),
    }.ToFrozenSet();

    [Fact]
    public void EveryUpstreamRecord_IsBoundBy_ExactlyOneClientMethod()
    {
        var binders = new Dictionary<Type, HashSet<MethodInfo>>();

        foreach (var (method, response) in DiscoverBoundResponses())
        {
            foreach (var upstreamType in ReachableOwnedTypes(response))
            {
                if (!binders.TryGetValue(upstreamType, out var boundBy))
                {
                    boundBy = [];
                    binders[upstreamType] = boundBy;
                }

                boundBy.Add(method);
            }
        }

        binders.Should().ContainKey(
            CollectionNestedUpstreamType,
            "the walk must reach past the top-level response into its collection item types — if it does " +
            "not, the rule silently passes over every nested record");

        var overShared = binders
            .Where(entry => entry.Value.Count > 1 && !SharedUpstreamTypes.Contains(entry.Key))
            .Select(entry =>
                $"{entry.Key.FullName} ({string.Join(", ", entry.Value.Select(m => $"{m.DeclaringType!.Name}.{m.Name}").Order(StringComparer.Ordinal))})")
            .Order(StringComparer.Ordinal)
            .ToList();

        overShared.Should().BeEmpty(
            "each upstream route owns the record the BFF binds it to, carrying only what that route's page " +
            "renders (bff.md § 4.1). The fix is to copy the record for the second route and trim it to what " +
            "that page reads — not to relocate it. Share it only if a change to one binding site would " +
            "always be the same change at the other, and then name it in {0}. Bound by several methods: {1}",
            nameof(SharedUpstreamTypes),
            string.Join("; ", overShared));
    }

    /// <summary>Every <c>Task&lt;Result&lt;T&gt;&gt;</c> client method, paired with its bound <c>T</c>.</summary>
    private static List<(MethodInfo Method, Type Response)> DiscoverBoundResponses()
    {
        var bound = new List<(MethodInfo, Type)>();

        foreach (var contract in InfrastructureAssembly.GetTypes().Where(type => type.IsInterface))
        {
            foreach (var method in contract.GetMethods())
            {
                if (ResponseTypeOf(method.ReturnType) is { } response
                    && response.Assembly == InfrastructureAssembly)
                {
                    bound.Add((method, response));
                }
            }
        }

        return bound;
    }

    /// <summary>Unwraps <c>Task&lt;Result&lt;T&gt;&gt;</c> to <c>T</c>; null for anything else, which is
    /// what excludes the value-less <c>Task&lt;Result&gt;</c> and non-client interfaces.</summary>
    private static Type? ResponseTypeOf(Type returnType)
    {
        if (!returnType.IsGenericType || returnType.GetGenericTypeDefinition() != typeof(Task<>))
        {
            return null;
        }

        var awaited = returnType.GetGenericArguments()[0];
        return awaited.IsGenericType && awaited.GetGenericTypeDefinition() == typeof(Result<>)
            ? awaited.GetGenericArguments()[0]
            : null;
    }

    private static HashSet<Type> ReachableOwnedTypes(Type seed)
    {
        var reached = new HashSet<Type>();
        var pending = new Stack<Type>();
        pending.Push(seed);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!reached.Add(current))
            {
                continue;
            }

            foreach (var property in current.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (var owned in OwnedTypesIn(property.PropertyType))
                {
                    pending.Push(owned);
                }
            }
        }

        return reached;
    }

    private static IEnumerable<Type> OwnedTypesIn(Type type)
    {
        // Bounding on the BFF's own assembly keeps Guid, string and IReadOnlyList<T> — which every client
        // method shares — out of the rule's subject.
        if (type.Assembly == InfrastructureAssembly)
        {
            yield return type;
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                foreach (var owned in OwnedTypesIn(argument))
                {
                    yield return owned;
                }
            }
        }
    }
}
