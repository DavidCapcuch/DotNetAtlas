using Inventory.Application.Common.Data;
using Inventory.Infrastructure.Persistence.EventStore;
using NetArchTest.Rules;

namespace Inventory.ArchitectureTests.BoundedContext;

/// <summary>
/// The Inventory event store is append-only by construction (<c>inventory.md</c> § 8 +
/// ADR-0006). The <see cref="IEventStore"/> port and its <see cref="EventStoreRepository"/>
/// implementation MUST expose only <c>RehydrateAsync</c> + <c>AppendAsync</c>. Adding any
/// <c>UpdateAsync</c> / <c>DeleteAsync</c> / <c>RemoveAsync</c> / <c>ReplaceAsync</c> would
/// break the immutable-stream invariant on which optimistic concurrency (<c>PK(StreamId,
/// Version)</c>) and projection rebuild rely.
/// </summary>
/// <remarks>
/// Pinned carry-forward — see <c>inventory.md:118</c> "Architecture-test for
/// 'append-only on stock_events'". The rule covers BOTH the port (<see cref="IEventStore"/>
/// in Inventory.Application) and the implementation (<see cref="EventStoreRepository"/> in
/// Inventory.Infrastructure) — a future contributor adding a mutating method to either
/// surface fails this rule.
/// </remarks>
public class EventStoreAppendOnlyTests : BaseTest
{
    private static readonly string[] AllowedPublicMethods = ["RehydrateAsync", "AppendAsync"];

    [Fact]
    public void IEventStore_PublicMethods_Should_BeSubsetOf_RehydrateAndAppend()
    {
        // Anchor first: a rename of the port would otherwise let the rule pass vacuously
        // (NetArchTest's MeetCustomRule on a zero-type filter returns FailingTypes=[]).
        var matchedTypes = Types.InAssembly(ApplicationAssembly)
            .That()
            .HaveName(nameof(IEventStore))
            .GetTypes()
            .ToList();

        matchedTypes.Should().ContainSingle(
            "the append-only rule is meaningless if IEventStore is renamed or moved out of " +
            "Inventory.Application — the rename itself is the architectural conversation we want to force.");

        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .HaveName(nameof(IEventStore))
            .Should()
            .MeetCustomRule(new PublicMethodsAreSubsetOfRule(AllowedPublicMethods))
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "IEventStore must expose only RehydrateAsync + AppendAsync. The event store is " +
            "append-only by construction (inventory.md § 8 + ADR-0006); adding UpdateAsync / " +
            "DeleteAsync / RemoveAsync / ReplaceAsync would break the immutable-stream invariant.");
    }

    [Fact]
    public void EventStoreRepository_PublicMethods_Should_BeSubsetOf_RehydrateAndAppend()
    {
        // Same anchor pattern as the port rule above.
        var matchedTypes = Types.InAssembly(InfrastructureAssembly)
            .That()
            .HaveName(nameof(EventStoreRepository))
            .GetTypes()
            .ToList();

        matchedTypes.Should().ContainSingle(
            "the append-only rule is meaningless if EventStoreRepository is renamed or moved out of " +
            "Inventory.Infrastructure — the rename itself is the architectural conversation we want to force.");

        var result = Types.InAssembly(InfrastructureAssembly)
            .That()
            .HaveName(nameof(EventStoreRepository))
            .Should()
            .MeetCustomRule(new PublicMethodsAreSubsetOfRule(AllowedPublicMethods))
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "EventStoreRepository must expose only RehydrateAsync + AppendAsync (the IEventStore " +
            "surface). Mutating methods on the repository would silently break append-only even if " +
            "the port stays clean.");
    }
}
