using Basket.Application.Baskets.Common.Contracts;
using Platform.CQRS;

namespace Basket.Application.Baskets.Checkout;

/// <summary>
/// Terminal transition for the basket owned by <paramref name="UserId"/>. Writes a
/// <c>BasketCheckoutInitiatedEvent</c> to the transactional outbox (topic
/// <c>basket.sessions</c>, key = UserId) and deletes the Redis entry on SQL-commit
/// success. The handler pre-assigns the Order's <c>OrderId</c> (UUID v7) and returns
/// it on success — it is the Order's identity from birth and becomes the downstream
/// Checkout Saga's correlation id (ADR-0029).
/// </summary>
/// <param name="UserId">Basket owner (JWT <c>sub</c>).</param>
/// <param name="ShippingAddress">Shipping address — pass-through courier data per ADR-0005.</param>
/// <param name="BillingAddress">Billing address; may equal <paramref name="ShippingAddress"/>.</param>
/// <param name="PaymentMethodId">Reference to a saved payment method in the Payments service.</param>
public sealed record CheckoutBasketCommand(
    Guid UserId,
    CheckoutAddressDto ShippingAddress,
    CheckoutAddressDto BillingAddress,
    Guid PaymentMethodId) : ICommand<Guid>;
