using Ardalis.Specification;

namespace Ordering.Domain.Orders.Specifications;

/// <summary>
/// Lists orders for a given buyer, optionally filtered by status, with
/// offset / limit paging (Appendix B.4 default). Most-recent-first.
/// </summary>
/// <remarks>
/// Keyset paging is out of scope for v1; <c>Skip</c> / <c>Take</c> is fine
/// at expected per-buyer order counts. A keyset-based successor may replace
/// this spec in a future milestone.
/// </remarks>
public sealed class OrdersByBuyerSpec : Specification<Order>
{
    public OrdersByBuyerSpec(Guid buyerId, OrderStatus? status, int skip, int take)
    {
        Query
            .Where(o => o.BuyerId == buyerId);

        if (status is not null)
        {
            Query.Where(o => o.Status == status);
        }

        // Deterministic paging: primary by recency, tie-broken by id so two
        // orders with equal CreatedAtUtc (rare but possible at sub-ms
        // resolution) never drop or duplicate across pages (use-cases.md § 3.4.2).
        Query
            .OrderByDescending(o => o.CreatedAtUtc)
            .ThenByDescending(o => o.Id)
            .Skip(skip)
            .Take(take)
            .TagWith(nameof(OrdersByBuyerSpec));
    }
}
