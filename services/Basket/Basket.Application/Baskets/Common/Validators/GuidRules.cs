using FluentValidation;

namespace Basket.Application.Baskets.Common.Validators;

/// <summary>
/// Shared FluentValidation rule extensions for GUID invariants used across Basket
/// command validators.
/// </summary>
internal static class GuidRules
{
    /// <summary>
    /// Asserts the GUID is RFC 9562 Version-7 (time-sortable). Used for
    /// <c>CheckoutBasketCommand.CorrelationId</c> per <c>use-cases.md § 2.1.6</c> —
    /// the Checkout saga relies on CorrelationIds being monotonic for tracing.
    /// </summary>
    public static IRuleBuilderOptions<T, Guid> MustBeVersion7<T>(this IRuleBuilder<T, Guid> rule)
        => rule.Must(g => g != Guid.Empty && g.Version == 7)
            .WithMessage("{PropertyName} must be a RFC 9562 Version-7 GUID (time-sortable).");
}
