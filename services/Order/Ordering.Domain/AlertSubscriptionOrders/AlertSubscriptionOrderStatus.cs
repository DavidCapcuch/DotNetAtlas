using Ardalis.SmartEnum;

namespace Ordering.Domain.AlertSubscriptionOrders;

/// <summary>
/// Smart enum representing the status of a subscription order through its lifecycle.
/// Encapsulates valid state transitions via <see cref="CanTransitionTo"/>.
/// </summary>
public sealed class AlertSubscriptionOrderStatus : SmartEnum<AlertSubscriptionOrderStatus>
{
    public static readonly AlertSubscriptionOrderStatus Initiated = new(nameof(Initiated), 0);
    public static readonly AlertSubscriptionOrderStatus Completed = new(nameof(Completed), 1);
    public static readonly AlertSubscriptionOrderStatus Failed = new(nameof(Failed), 2);

    private AlertSubscriptionOrderStatus(string name, int value)
        : base(name, value)
    {
    }

    /// <summary>
    /// Determines whether a transition from this status to the target status is valid.
    /// </summary>
    /// <param name="target">The target status to transition to.</param>
    /// <returns><c>true</c> if the transition is valid; otherwise, <c>false</c>.</returns>
    /// <remarks>
    /// Valid transitions:
    /// <list type="bullet">
    /// <item><see cref="Initiated"/> → <see cref="Completed"/></item>
    /// <item><see cref="Initiated"/> → <see cref="Failed"/></item>
    /// </list>
    /// <see cref="Completed"/> and <see cref="Failed"/> are terminal states with no valid outgoing transitions.
    /// </remarks>
    public bool CanTransitionTo(AlertSubscriptionOrderStatus target)
    {
        return this == Initiated && (target == Completed || target == Failed);
    }
}
