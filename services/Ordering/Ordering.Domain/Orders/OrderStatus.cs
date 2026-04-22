using System.Collections.Immutable;
using Ardalis.SmartEnum;

namespace Ordering.Domain.Orders;

/// <summary>
/// Lifecycle status of an <see cref="Order"/>. Transitions are guarded by
/// <see cref="CanTransitionTo"/> and follow the FSM in
/// <c>docs/bc-design/ordering.md § 5.1</c> (stock-before-payment per ADR-0004).
/// </summary>
/// <remarks>
/// Canonical happy path:
/// <c>Created → StockReserved → PaymentCompleted → Confirmed → Shipped → Delivered</c>.
/// Off-ramps: <c>Cancelled</c> from any non-terminal non-shipped state;
/// <c>Failed</c> from <c>Created</c>, <c>StockReserved</c>, <c>PaymentCompleted</c> only.
/// </remarks>
public sealed class OrderStatus : SmartEnum<OrderStatus>
{
    public static readonly OrderStatus Created = new(nameof(Created), 0, isTerminal: false);
    public static readonly OrderStatus StockReserved = new(nameof(StockReserved), 1, isTerminal: false);
    public static readonly OrderStatus PaymentCompleted = new(nameof(PaymentCompleted), 2, isTerminal: false);
    public static readonly OrderStatus Confirmed = new(nameof(Confirmed), 3, isTerminal: false);
    public static readonly OrderStatus Shipped = new(nameof(Shipped), 4, isTerminal: false);
    public static readonly OrderStatus Delivered = new(nameof(Delivered), 5, isTerminal: true);
    public static readonly OrderStatus Cancelled = new(nameof(Cancelled), 6, isTerminal: true);
    public static readonly OrderStatus Failed = new(nameof(Failed), 7, isTerminal: true);

    private static readonly Lazy<IReadOnlyDictionary<OrderStatus, ImmutableHashSet<OrderStatus>>> Transitions =
        new(BuildTransitionTable);

    /// <summary>
    /// True when no further transitions are possible from this status.
    /// </summary>
    public bool IsTerminal { get; }

    private OrderStatus(string name, int value, bool isTerminal)
        : base(name, value)
    {
        IsTerminal = isTerminal;
    }

    /// <summary>
    /// Returns whether this status can transition to <paramref name="target"/>.
    /// Self-transition and any transition out of a terminal status return <c>false</c>.
    /// </summary>
    /// <param name="target">The target status.</param>
    public bool CanTransitionTo(OrderStatus target)
    {
        ArgumentNullException.ThrowIfNull(target);

        return Transitions.Value.TryGetValue(this, out var allowed) && allowed.Contains(target);
    }

    private static Dictionary<OrderStatus, ImmutableHashSet<OrderStatus>> BuildTransitionTable()
    {
        return new Dictionary<OrderStatus, ImmutableHashSet<OrderStatus>>
        {
            [Created] = [StockReserved, Cancelled, Failed],
            [StockReserved] = [PaymentCompleted, Cancelled, Failed],
            [PaymentCompleted] = [Confirmed, Cancelled, Failed],
            [Confirmed] = [Shipped, Cancelled],
            [Shipped] = [Delivered],
            [Delivered] = [],
            [Cancelled] = [],
            [Failed] = [],
        };
    }
}
