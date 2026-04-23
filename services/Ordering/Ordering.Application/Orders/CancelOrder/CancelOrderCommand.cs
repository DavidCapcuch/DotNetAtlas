using Platform.CQRS;

namespace Ordering.Application.Orders.CancelOrder;

/// <summary>
/// Cancels an <c>Order</c>. Invoked from the admin HTTP endpoint (M5) or
/// from the Checkout saga on compensation. Authorization: buyer cancels
/// their own order; admin cancels any order. <see cref="IsAdmin"/> is
/// derived from the JWT role claim at the endpoint and is always
/// <c>false</c> for saga-originated calls (saga ⇒ buyer ownership assumed
/// and correlation-id already matches).
/// </summary>
/// <remarks>
/// Cancellation after <c>Shipped</c> or <c>Delivered</c> surfaces as
/// <c>OrderingErrors.CannotCancelInStatus</c> (user error, 409). Missing
/// order or not-owned-by-buyer both surface as
/// <c>OrderingErrors.OrderNotFound</c> — do NOT leak existence.
/// </remarks>
public sealed class CancelOrderCommand : ICommand
{
    public required Guid OrderId { get; init; }

    public required string Reason { get; init; }

    public required Guid BuyerId { get; init; }

    public required bool IsAdmin { get; init; }
}
