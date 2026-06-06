using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace Inventory.ArchitectureTests.BoundedContext;

/// <summary>
/// ADR-0034 / ADR-0016 invariants for the Inventory-owned read-through stock cache:
/// it lives on <c>redis-cache</c> (never <c>redis-basket</c>), and the reservation
/// decision path is oversell-safe by construction — it never depends on the display cache.
/// </summary>
public sealed class StockAvailabilityCacheTests : BaseTest
{
    private const string StockLevelCacheInterface = "Inventory.Application.StockItems.Common.IStockLevelCache";

    /// <summary>
    /// ADR-0016 connection-string discipline: the stock cache wiring binds the
    /// <c>Redis:Cache</c> connection string and MUST NOT touch <c>Redis:Basket</c>
    /// (the authoritative basket store). A cross-use would silently couple the volatile
    /// display cache to the durable basket instance.
    /// </summary>
    [Fact]
    public void CacheWiring_UsesRedisCache_NotRedisBasket()
    {
        var noBasketReference = Types.InAssembly(InfrastructureAssembly)
            .Should()
            .MeetCustomRule(new DoesNotLoadStringContainingRule("Redis:Basket"))
            .GetResult();

        noBasketReference.IsSuccessful.Should().BeTrue(
            "no Inventory.Infrastructure type may reference the 'Redis:Basket' connection string (ADR-0016): {0}",
            string.Join(", ", noBasketReference.FailingTypes?.Select(t => t.Name) ?? []));

        var bindsRedisCache = Types.InAssembly(InfrastructureAssembly)
            .That()
            .HaveName("CacheDependencyInjection")
            .Should()
            .MeetCustomRule(new LoadsStringRule("Redis:Cache"))
            .GetResult();

        bindsRedisCache.IsSuccessful.Should().BeTrue(
            "CacheDependencyInjection must bind the 'Redis:Cache' connection string (ADR-0034 + ADR-0016)");
    }

    /// <summary>
    /// Oversell safety is structural (ADR-0034 / ADR-0006): the reservation decision rehydrates
    /// the event-sourced aggregate, so the command path must NOT depend on the display cache.
    /// A future contributor wiring <c>IStockLevelCache</c> into the reserve path fails here.
    /// </summary>
    [Fact]
    public void ReserveStockCommandHandler_DoesNotDependOnStockLevelCache()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .HaveName("ReserveStockCommandHandler")
            .Should()
            .NotHaveDependencyOnAny(StockLevelCacheInterface)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "the reservation decision must read the event-sourced aggregate, never the display cache (ADR-0034)");
    }

    /// <summary>
    /// The event-store write path (rehydrate + append) likewise never reads the display cache —
    /// the cache lives entirely on the query side.
    /// </summary>
    [Fact]
    public void EventStoreRepository_DoesNotDependOnStockLevelCache()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .That()
            .HaveName("EventStoreRepository")
            .Should()
            .NotHaveDependencyOnAny(StockLevelCacheInterface)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "the event-store write path is the authoritative reservation source; it must not read the cache (ADR-0034)");
    }

    /// <summary>Fails a type that loads a string literal containing <paramref name="fragment"/> anywhere in its IL (incl. nested closures).</summary>
    private sealed class DoesNotLoadStringContainingRule(string fragment) : ICustomRule
    {
        public bool MeetsRule(TypeDefinition type)
        {
            foreach (var method in AllMethods(type))
            {
                if (!method.HasBody)
                {
                    continue;
                }

                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode == OpCodes.Ldstr &&
                        instruction.Operand is string value &&
                        value.Contains(fragment, StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }

    /// <summary>Passes only when the type loads the exact string <paramref name="required"/> somewhere in its IL.</summary>
    private sealed class LoadsStringRule(string required) : ICustomRule
    {
        public bool MeetsRule(TypeDefinition type)
        {
            foreach (var method in AllMethods(type))
            {
                if (!method.HasBody)
                {
                    continue;
                }

                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode == OpCodes.Ldstr &&
                        instruction.Operand is string value &&
                        string.Equals(value, required, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    private static IEnumerable<MethodDefinition> AllMethods(TypeDefinition type)
    {
        foreach (var method in type.Methods)
        {
            yield return method;
        }

        foreach (var nested in type.NestedTypes)
        {
            foreach (var method in AllMethods(nested))
            {
                yield return method;
            }
        }
    }
}
