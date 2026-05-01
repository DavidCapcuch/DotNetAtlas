namespace Ordering.IntegrationTests.Sessions;

/// <summary>
/// Session 3 of <c>docs/bc-design/example-mapping/ordering.md</c> documents
/// item-immutability future-guards (R1: items locked after <c>StockReserved</c>;
/// R5: addresses, buyer, correlation id, and total likewise immutable
/// after creation). V1 ships with NO item-mutation commands on the
/// <c>Order</c> aggregate (per-item state is set inside
/// <c>Order.CreateFromBasket</c> and frozen — <c>Items</c> is exposed as
/// <c>IReadOnlyCollection&lt;OrderItem&gt;</c> and there are no
/// <c>AddItem</c> / <c>RemoveItem</c> / <c>ChangeQuantity</c> commands),
/// so the rule is trivially satisfied and there is no integration entry
/// point through which to exercise it. The aggregate-level guards on
/// hypothetical future commands are documented in the design doc and
/// will be enforced when those commands are introduced.
/// </summary>
public sealed class ItemImmutabilityIntegrationTests
{
    [Fact(Skip = "Session 3 future-guard — no v1 commands mutate items; see class summary for rationale.")]
    public void Placeholder_ItemMutationGuard_NotApplicableInV1()
    {
        // Intentionally empty. See class-level summary. No collection
        // fixture is needed: the only fact is Skip'd and xUnit will not
        // instantiate this class.
    }
}
