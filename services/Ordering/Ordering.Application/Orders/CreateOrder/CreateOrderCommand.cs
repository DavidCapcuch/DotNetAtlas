using Platform.CQRS;

namespace Ordering.Application.Orders.CreateOrder;

/// <summary>
/// Saga-issued command to create a new <c>Order</c> from a frozen basket
/// snapshot at checkout time. Originates from the Checkout saga's
/// <c>CreateOrderCommand</c> Avro message on <c>ordering.order-commands</c>;
/// the Kafka consumer translates the Avro record to this internal DTO and
/// dispatches via <see cref="ICommand{TResponse}"/>.
/// </summary>
/// <remarks>
/// Returns the created <c>OrderId</c> so the saga can correlate the follow-up
/// <c>OrderCreatedEvent</c> and arm its subsequent timeouts. The handler is
/// idempotent on <see cref="CorrelationId"/> (see
/// <see cref="CreateOrderCommandHandler"/>).
/// </remarks>
public sealed record CreateOrderCommand : ICommand<Guid>
{
    /// <summary>Checkout saga correlation id. Becomes <c>Order.CorrelationId</c>.</summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>JWT <c>sub</c> claim of the buyer placing the order.</summary>
    public required Guid BuyerId { get; init; }

    /// <summary>Payment method reference held by the Payments bounded context.</summary>
    public required Guid PaymentMethodId { get; init; }

    /// <summary>ISO 4217 currency code shared by all items (single-currency invariant I-9).</summary>
    public required string Currency { get; init; }

    /// <summary>Frozen basket line items — at least one, positive quantity and unit price.</summary>
    public required IReadOnlyList<CreateOrderItemInput> Items { get; init; }

    /// <summary>Shipping address (PII — see ADR-0011).</summary>
    public required AddressInput ShippingAddress { get; init; }

    /// <summary>Billing address (PII — see ADR-0011).</summary>
    public required AddressInput BillingAddress { get; init; }

    /// <summary>UTC timestamp when the saga issued the command.</summary>
    public required DateTimeOffset RequestedAtUtc { get; init; }
}

/// <summary>One line of a <see cref="CreateOrderCommand"/>.</summary>
public sealed record CreateOrderItemInput(
    Guid ProductId,
    string Sku,
    string Name,
    int Quantity,
    decimal UnitPriceAmount);

/// <summary>Address POCO used by <see cref="CreateOrderCommand"/>. Translated to
/// <c>Platform.SharedKernel.ValueObjects.Address</c> via <c>Address.Create</c>
/// in the handler; validator replicates the shared-kernel shape rules for an
/// early 400.</summary>
public sealed record AddressInput(
    string Street1,
    string? Street2,
    string City,
    string? State,
    string PostalCode,
    string CountryCode);
