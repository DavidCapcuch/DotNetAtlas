namespace Ordering.Application.Orders.GetOrdersByBuyer;

/// <summary>
/// Paged envelope for a buyer's orders. Items are a summary projection
/// (<c>use-cases.md § 3.4.2</c>) distinct from the full
/// <c>GetOrderByIdResponse</c> — the list endpoint deliberately ships less
/// per row than the detail endpoint.
/// </summary>
public sealed class GetOrdersByBuyerResponse
{
    public required IReadOnlyList<OrderSummaryDto> Items { get; init; }

    public required int Total { get; init; }

    public required int PageNumber { get; init; }

    public required int PageSize { get; init; }
}

/// <summary>
/// Single-row summary for the buyer-orders list view.
/// <see cref="LastStatusChangeAtUtc"/> is
/// <c>COALESCE(DeliveredAtUtc, ShippedAtUtc, ConfirmedAtUtc, PaymentCompletedAtUtc, StockReservedAtUtc, CreatedAtUtc)</c>
/// projected SQL-side — the most-recent non-null lifecycle timestamp.
/// </summary>
public sealed record OrderSummaryDto(
    Guid OrderId,
    string Status,
    decimal TotalAmount,
    string Currency,
    int ItemCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastStatusChangeAtUtc);
