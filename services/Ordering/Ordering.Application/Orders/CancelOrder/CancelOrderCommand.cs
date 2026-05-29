using Platform.CQRS;

namespace Ordering.Application.Orders.CancelOrder;

/// <summary>
/// Cancels an <c>Order</c>. Invoked from two surfaces: the admin HTTP
/// endpoint and the Checkout saga (<c>CancelOrderCommand</c> on
/// <c>ordering.order-commands</c>). The caller decides the authorisation
/// mode via <see cref="IsAdmin"/> + <see cref="BuyerId"/>:
/// <list type="bullet">
/// <item>
/// HTTP buyer cancel: <c>IsAdmin=false</c>, <c>BuyerId=JWT sub</c>.
/// Cross-buyer access returns <c>OrderingErrors.OrderNotFound</c>
/// (existence hidden per <c>ordering.md § 9.2</c>).
/// </item>
/// <item>
/// HTTP admin cancel: <c>IsAdmin=true</c>, <c>BuyerId=Guid.Empty</c>.
/// Authorised by the <c>AuthPolicies.OrderingAdmin</c> policy.
/// </item>
/// <item>
/// Saga compensation: <c>IsAdmin=true</c>, <c>BuyerId=Guid.Empty</c>.
/// The saga is a trusted privileged caller; saga-issued commands are not
/// separately authorised at the message layer — the trust boundary is the
/// deployment network (ADR-0010).
/// </item>
/// </list>
/// </summary>
/// <remarks>
/// Cancellation after <c>Shipped</c> or <c>Delivered</c> surfaces as
/// <c>OrderingErrors.CannotCancelInStatus</c> (user error, 409). Missing
/// order surfaces as <c>OrderingErrors.OrderNotFound</c>.
/// </remarks>
public sealed record CancelOrderCommand : ICommand
{
    public required Guid OrderId { get; init; }

    public required string Reason { get; init; }

    public required Guid BuyerId { get; init; }

    public required bool IsAdmin { get; init; }
}
