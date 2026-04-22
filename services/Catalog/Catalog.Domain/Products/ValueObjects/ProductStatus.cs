using Ardalis.SmartEnum;

namespace Catalog.Domain.Products.ValueObjects;

/// <summary>
/// Smart enum representing the commercial lifecycle of a <see cref="Product"/>.
/// Governs sellability and transitions through <see cref="CanTransitionTo"/>.
/// </summary>
public sealed class ProductStatus : SmartEnum<ProductStatus>
{
    public static readonly ProductStatus Draft = new(nameof(Draft), 0, isSellable: false, isTerminal: false);
    public static readonly ProductStatus Active = new(nameof(Active), 1, isSellable: true, isTerminal: false);
    public static readonly ProductStatus Discontinued = new(nameof(Discontinued), 2, isSellable: false, isTerminal: false);

    /// <summary>
    /// True when products in this status may be referenced by Basket.
    /// </summary>
    public bool IsSellable { get; }

    /// <summary>
    /// True when no further transitions are possible.
    /// Discontinued is reactivatable (with admin flag) so no status is terminal in v1.
    /// </summary>
    public bool IsTerminal { get; }

    private ProductStatus(string name, int value, bool isSellable, bool isTerminal)
        : base(name, value)
    {
        IsSellable = isSellable;
        IsTerminal = isTerminal;
    }

    /// <summary>
    /// Returns whether this status can transition to <paramref name="target"/>.
    /// Transitioning to the current status is treated as a no-op (returns <c>false</c> —
    /// callers should guard against no-op moves separately).
    /// </summary>
    /// <param name="target">The target status.</param>
    /// <param name="adminReactivation">
    /// Must be <c>true</c> to allow the Discontinued → Active transition.
    /// Ignored for all other transitions.
    /// </param>
    public bool CanTransitionTo(ProductStatus target, bool adminReactivation = false)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (this == target)
        {
            return false;
        }

        if (this == Draft && target == Active)
        {
            return true;
        }

        if (this == Active && target == Discontinued)
        {
            return true;
        }

        if (this == Discontinued && target == Active)
        {
            return adminReactivation;
        }

        return false;
    }
}
