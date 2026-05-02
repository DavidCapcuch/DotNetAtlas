using System.Reflection;
using Ordering.Domain.Orders.ValueObjects;

namespace Ordering.ArchitectureTests;

/// <summary>
/// Cross-BC contract test guarding the Basket → Ordering ProductSnapshot boundary.
/// Tracks finding F6 from the 2026-04-25 technical design review:
/// Ordering's <see cref="ProductSnapshot"/> must be a structural superset of
/// Basket's <c>ProductSnapshot</c> on the audit-relevant fields. Today it isn't —
/// <c>CapturedAtUtc</c> is dropped at the ACL boundary, discarding the
/// "when did the user see this price?" answer needed for chargebacks +
/// price-change disputes.
/// </summary>
/// <remarks>
/// <para>
/// Implementation chain required to unskip these tests:
/// </para>
/// <list type="number">
///   <item>Add <c>CapturedAtUtc</c> field to
///   <c>platform/Platform.SchemaRegistry.Contracts/Avro/Basket/Sessions/BasketCheckoutInitiatedEvent.avsc</c>
///   on the <c>BasketCheckoutItem</c> record (FORWARD_TRANSITIVE compat per
///   ADR-0007 — the field must be nullable with a default).</item>
///   <item>Re-run avrogen.</item>
///   <item>Propagate the field through Basket's
///   <c>BasketCheckoutInitiatedMapper</c> (Application layer).</item>
///   <item>Add <c>CapturedAtUtc</c> to <c>CreateOrderCommand</c>.<c>OrderItemInput</c>
///   and propagate via the saga's <c>CreateOrderConsumer</c>.</item>
///   <item>Add <c>CapturedAtUtc</c> to
///   <c>Ordering.Domain.Baskets.BasketSnapshotItem</c>.</item>
///   <item>Add <c>CapturedAtUtc</c> to
///   <c>Ordering.Domain.Orders.ValueObjects.ProductSnapshot</c> (this assembly's type)
///   with a <c>required init</c> property + validation in <c>Create</c>.</item>
///   <item>Update <c>Order.CreateFromBasket</c> caller to pass
///   <c>basketItem.CapturedAtUtc</c> through.</item>
///   <item>Add EF column mapping in
///   <c>Ordering.Infrastructure/Persistence/Database/EntityConfigurations/Orders/OrderConfiguration.cs</c>.
///   <strong>User generates the migration</strong> — see CLAUDE.md.</item>
///   <item>Update unit / integration tests to construct snapshots with timestamps.</item>
///   <item>Remove the <c>Skip</c> on the facts below.</item>
/// </list>
/// <para>
/// Tracking: ADR-0002 (pricing in Catalog) explains why frozen snapshots exist;
/// docs/bc-design/ordering.md and docs/implementation-prompts/ordering.md call
/// out the gap and the fix.
/// </para>
/// </remarks>
public sealed class ProductSnapshotContractTests
{
    private const string PendingChainSkip =
        "F6 pending — see class-level remarks for the implementation chain " +
        "(Avro → CreateOrderCommand → BasketSnapshotItem → Ordering.ProductSnapshot + EF migration).";

    /// <summary>
    /// Asserts <see cref="ProductSnapshot"/> exposes <c>CapturedAtUtc</c> at all.
    /// Skipped today; will fail loudly the moment the rest of the chain is wired
    /// without the timestamp landing on the aggregate.
    /// </summary>
    [Fact(Skip = PendingChainSkip)]
    public void OrderingProductSnapshot_HasCapturedAtUtc()
    {
        var capturedAtUtc = typeof(ProductSnapshot).GetProperty(
            "CapturedAtUtc",
            BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(capturedAtUtc);
        Assert.Equal(typeof(DateTimeOffset), capturedAtUtc!.PropertyType);
    }

    /// <summary>
    /// Structural-superset rule: every public instance property on Basket's
    /// <c>ProductSnapshot</c> must also exist on Ordering's <see cref="ProductSnapshot"/>
    /// with the same name. Loaded via reflection-by-name to avoid a direct
    /// project reference from Ordering tests to Basket.Domain (cross-BC ref
    /// would violate Ordering.ArchitectureTests' isolation rule). The Basket
    /// assembly is expected next to this test assembly's bin output once
    /// it's added to the csproj's <c>ProjectReference</c> list as part of
    /// the chain implementation.
    /// </summary>
    [Fact(Skip = PendingChainSkip)]
    public void OrderingProductSnapshot_IsStructuralSupersetOfBasketProductSnapshot()
    {
        var basketAssembly = Assembly.Load("Basket.Domain");
        var basketSnapshot = basketAssembly.GetType(
            "Basket.Domain.Baskets.ValueObjects.ProductSnapshot",
            throwOnError: true)!;

        var basketProps = basketSnapshot
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var orderingProps = typeof(ProductSnapshot)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var missing = basketProps.Except(orderingProps).ToList();
        Assert.True(
            missing.Count == 0,
            "Ordering.ProductSnapshot is missing Basket.ProductSnapshot fields: " +
            string.Join(", ", missing) +
            ". Audit fidelity across the Basket → Ordering ACL must be preserved (F6).");
    }
}
