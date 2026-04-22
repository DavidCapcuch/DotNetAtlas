using Platform.SharedKernel.Errors;

namespace Basket.Domain.Baskets.Errors;

/// <summary>
/// Validation errors raised by the <see cref="ValueObjects.BasketItem"/> value object.
/// Kept separate from <see cref="BasketErrors"/> because <c>BasketItem</c> is a VO
/// that can be constructed outside an aggregate context.
/// </summary>
public static class BasketItemErrors
{
    public static ValidationError InvalidQuantity()
        => new ValidationError(
            propertyName: "Quantity",
            errorMessage: "Quantity must be at least 1.",
            errorCode: "BasketItem.InvalidQuantity");
}
