using System.Collections.Frozen;
using System.Reflection;
using EShop.BFF.Api.Responses;
using FastEndpoints;

namespace EShop.BFF.ArchitectureTests.ApiContracts;

/// <summary>
/// ADR-0037: each endpoint owns its published wire contract. Every type reachable from an endpoint's
/// response belongs to exactly one endpoint, except the types in <see cref="SharedWireTypes"/>.
/// </summary>
public sealed class EndpointOwnedResponseContractTests : BaseTest
{
    // Per-unit configuration — the only part that changes when this file is copied to another unit.

    /// <summary>A type this unit reaches only through a collection, so a walk that stopped at the
    /// response envelopes fails here instead of passing over an unchecked contract.</summary>
    private static readonly Type CollectionNestedWireType = typeof(BasketPageItemDto);

    private static readonly Assembly EndpointAssembly = ApiAssembly;

    private static readonly FrozenSet<Assembly> OwnedAssemblies =
        new[] { ApiAssembly, InfrastructureAssembly }.ToFrozenSet();

    /// <summary>ADR-0037 § Implementation Notes carries the per-type rulings behind each entry.</summary>
    private static readonly FrozenSet<Type> SharedWireTypes = new[]
    {
        typeof(MoneyDto),
    }.ToFrozenSet();

    [Fact]
    public void EveryWireType_BelongsTo_ExactlyOneEndpoint()
    {
        // The endpoint set per wire type is what counts endpoints rather than reference sites: one
        // response reaching a type through two members adds the same endpoint twice.
        var owners = new Dictionary<Type, HashSet<Type>>();

        foreach (var (endpoint, response) in DiscoverResponseEndpoints())
        {
            foreach (var wireType in ReachableOwnedTypes(response))
            {
                if (!owners.TryGetValue(wireType, out var owningEndpoints))
                {
                    owningEndpoints = [];
                    owners[wireType] = owningEndpoints;
                }

                owningEndpoints.Add(endpoint);
            }
        }

        owners.Should().ContainKey(
            CollectionNestedWireType,
            "the walk must resolve responses and reach past their envelopes — if it does not, either " +
            "FastEndpoints' base-type shape changed or {0} is missing an assembly",
            nameof(OwnedAssemblies));

        var overShared = owners
            .Where(entry => entry.Value.Count > 1 && !SharedWireTypes.Contains(entry.Key))
            .Select(entry =>
                $"{entry.Key.FullName} ({string.Join(", ", entry.Value.Select(owner => owner.Name).Order(StringComparer.Ordinal))})")
            .Order(StringComparer.Ordinal)
            .ToList();

        overShared.Should().BeEmpty(
            "ADR-0037 gives each endpoint its own envelope and item types, so the fix is to copy the type " +
            "into the second endpoint's contract — not to relocate it. Share it only if it passes the " +
            "knowledge test (would a change to one site always require the same change to the other?), " +
            "and then name it in {0}. Shared across endpoints: {1}",
            nameof(SharedWireTypes),
            string.Join("; ", overShared));
    }

    private static List<(Type Endpoint, Type Response)> DiscoverResponseEndpoints()
    {
        var endpoints = new List<(Type, Type)>();

        foreach (var type in EndpointAssembly.GetTypes())
        {
            if (type is not { IsClass: true, IsAbstract: false } || ClosedEndpointBaseOf(type) is not { } endpointBase)
            {
                continue;
            }

            var response = endpointBase.GetGenericArguments()[1];
            if (OwnedAssemblies.Contains(response.Assembly))
            {
                endpoints.Add((type, response));
            }
        }

        return endpoints;
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

            // Properties are what crosses the wire, given ADR-0037's property-style records.
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
        // Bounding on the owned assemblies is what keeps Guid, string and IReadOnlyList<T> — which every
        // endpoint shares — out of the rule's subject, and why ProblemDetails needs no exemption.
        if (OwnedAssemblies.Contains(type.Assembly))
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

    /// <summary>Walking the base chain is what makes aliases like <c>EndpointWithoutRequest&lt;T&gt;</c>
    /// resolve to the <c>Endpoint&lt;TRequest, TResponse&gt;</c> every FastEndpoints endpoint ends at.</summary>
    private static Type? ClosedEndpointBaseOf(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(Endpoint<,>))
            {
                return current;
            }
        }

        return null;
    }
}
