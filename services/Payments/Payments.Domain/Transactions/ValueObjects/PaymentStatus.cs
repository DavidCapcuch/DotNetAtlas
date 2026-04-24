using System.Collections.Immutable;
using Ardalis.SmartEnum;

namespace Payments.Domain.Transactions.ValueObjects;

/// <summary>
/// Lifecycle status of a <see cref="PaymentTransaction"/>. Transitions are guarded by
/// <see cref="CanTransitionTo"/> and follow the FSM in
/// <c>docs/bc-design/payments.md § 4</c>.
/// </summary>
/// <remarks>
/// Canonical happy path:
/// <c>Requested → Authorized → Captured → Completed</c> (capture auto-advances to Completed).
/// Off-ramps: <c>Failed</c> from <c>Requested</c> or <c>Authorized</c>; <c>Voided</c> from
/// <c>Authorized</c> (pre-capture compensation); <c>Refunded</c> from <c>Captured</c> or
/// <c>Completed</c> (post-capture compensation).
/// </remarks>
public sealed class PaymentStatus : SmartEnum<PaymentStatus>
{
    public static readonly PaymentStatus Requested = new(nameof(Requested), 0, isFinal: false);
    public static readonly PaymentStatus Authorized = new(nameof(Authorized), 1, isFinal: false);
    public static readonly PaymentStatus Captured = new(nameof(Captured), 2, isFinal: false);
    public static readonly PaymentStatus Completed = new(nameof(Completed), 3, isFinal: false);
    public static readonly PaymentStatus Failed = new(nameof(Failed), 4, isFinal: true);
    public static readonly PaymentStatus Voided = new(nameof(Voided), 5, isFinal: true);
    public static readonly PaymentStatus Refunded = new(nameof(Refunded), 6, isFinal: true);

    private static readonly Lazy<IReadOnlyDictionary<PaymentStatus, ImmutableHashSet<PaymentStatus>>> Transitions =
        new(BuildTransitionTable);

    /// <summary>
    /// True when no further transitions are possible from this status.
    /// <see cref="Completed"/> is NOT final — it remains saga-reversible via <see cref="Refunded"/>
    /// (cancel-post-capture compensation path).
    /// </summary>
    public bool IsFinal { get; }

    private PaymentStatus(string name, int value, bool isFinal)
        : base(name, value)
    {
        IsFinal = isFinal;
    }

    /// <summary>
    /// Returns whether this status can transition to <paramref name="target"/>.
    /// Self-transition and any transition out of a final status return <c>false</c>.
    /// </summary>
    /// <param name="target">The target status.</param>
    public bool CanTransitionTo(PaymentStatus target)
    {
        ArgumentNullException.ThrowIfNull(target);

        return Transitions.Value.TryGetValue(this, out var allowed) && allowed.Contains(target);
    }

    private static Dictionary<PaymentStatus, ImmutableHashSet<PaymentStatus>> BuildTransitionTable()
    {
        return new Dictionary<PaymentStatus, ImmutableHashSet<PaymentStatus>>
        {
            [Requested] = [Authorized, Failed],
            [Authorized] = [Captured, Failed, Voided],
            [Captured] = [Completed, Refunded],
            [Completed] = [Refunded],
            [Failed] = [],
            [Voided] = [],
            [Refunded] = [],
        };
    }
}
