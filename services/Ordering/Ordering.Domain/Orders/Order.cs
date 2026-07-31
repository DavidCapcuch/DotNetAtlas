using FluentResults;
using Ordering.Domain.Baskets;
using Ordering.Domain.Errors;
using Ordering.Domain.Orders.Events;
using Ordering.Domain.Orders.ValueObjects;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Exceptions;
using Platform.SharedKernel.ValueObjects;

namespace Ordering.Domain.Orders;

/// <summary>
/// Aggregate root tracking the commitment to purchase from creation through
/// delivery or termination. The reference implementation of the
/// SmartEnum-guarded status FSM in this solution (<c>ordering.md § 3.1 + § 5.1</c>).
/// </summary>
/// <remarks>
/// <para>Invariants (full table in <c>ordering.md § 3.1</c>):</para>
/// <list type="bullet">
/// <item>I-1 status transitions gated by <see cref="OrderStatus.CanTransitionTo"/>.</item>
/// <item>I-2 items immutable after <c>StockReserved</c> — <b>future-guard</b>; v1 has no item-mutation commands, so the invariant is enforced by the absence of mutators.</item>
/// <item>I-3..I-5 addresses, buyer immutable after factory.</item>
/// <item>I-6 <see cref="Total"/> = Σ <c>OrderItem.LineTotal</c>, single currency.</item>
/// <item>I-7 at least one item at creation.</item>
/// <item>I-8 all line items have positive quantity and unit price.</item>
/// <item>I-9 single currency across all items.</item>
/// <item>I-10 ISO 3166-1 alpha-2 country code on addresses.</item>
/// <item>I-11 terminal statuses are terminal.</item>
/// <item>I-12 no cancellation after <c>Shipped</c> — the one user-visible error in the saga flow.</item>
/// </list>
/// <para>Domain events raised:</para>
/// <list type="bullet">
/// <item><see cref="OrderCreatedDomainEvent"/> from <see cref="CreateFromBasket"/>.</item>
/// <item><see cref="OrderStockReservedDomainEvent"/> from <see cref="MarkStockReserved"/>.</item>
/// <item><see cref="OrderPaymentCompletedDomainEvent"/> from <see cref="MarkPaymentCompleted"/>.</item>
/// <item><see cref="OrderConfirmedDomainEvent"/> from <see cref="Confirm"/>.</item>
/// <item><see cref="OrderShippedDomainEvent"/> from <see cref="MarkShipped"/>.</item>
/// <item><see cref="OrderDeliveredDomainEvent"/> from <see cref="MarkDelivered"/>.</item>
/// <item><see cref="OrderCancelledDomainEvent"/> from <see cref="Cancel"/>.</item>
/// <item><see cref="OrderFailedDomainEvent"/> from <see cref="Fail"/>.</item>
/// </list>
/// </remarks>
public sealed class Order : AggregateRoot<Guid>, IAuditableEntity
{
    private readonly List<OrderItem> _items = [];

    public Guid BuyerId { get; private set; }
    public Guid PaymentMethodId { get; private set; }
    public Guid? PaymentTransactionId { get; private set; }
    public Guid? StockReservationId { get; private set; }

    public IReadOnlyCollection<OrderItem> Items => _items;

    public Address ShippingAddress { get; private set; } = null!;
    public Address BillingAddress { get; private set; } = null!;

    public OrderStatus Status { get; private set; } = null!;
    public Money Total { get; private set; } = null!;

    public CancellationInfo? Cancellation { get; private set; }
    public FailureInfo? Failure { get; private set; }
    public ShipmentInfo? Shipment { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? StockReservedAtUtc { get; private set; }
    public DateTimeOffset? PaymentCompletedAtUtc { get; private set; }
    public DateTimeOffset? ConfirmedAtUtc { get; private set; }
    public DateTimeOffset? DeliveredAtUtc { get; private set; }

    public DateTimeOffset CreatedUtc { get; private set; }
    public DateTimeOffset LastModifiedUtc { get; private set; }

    private Order()
    {
    }

    /// <summary>
    /// Creates a new <see cref="Order"/> in <see cref="OrderStatus.Created"/>
    /// from a <see cref="BasketSnapshot"/>. The <paramref name="orderId"/> is
    /// client-assigned (pre-allocated at checkout initiation, UUID v7) and
    /// persisted as the aggregate identity rather than minted here — see
    /// ADR-0029. Invariants I-6..I-9 are enforced here as bug-class
    /// (<see cref="DataIntegrityException"/>): Basket / BFF should have already
    /// validated them, so a failure reaching this factory is a system bug, not
    /// a user error.
    /// </summary>
    /// <remarks>
    /// Raises <see cref="OrderCreatedDomainEvent"/> on success.
    /// </remarks>
    public static Order CreateFromBasket(
        Guid orderId,
        Guid buyerId,
        BasketSnapshot basket,
        Address shippingAddress,
        Address billingAddress,
        Guid paymentMethodId,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(basket);
        ArgumentNullException.ThrowIfNull(shippingAddress);
        ArgumentNullException.ThrowIfNull(billingAddress);

        // H-1 — symmetric with the other null/empty guards below.
        if (basket.Currency is null)
        {
            throw new DataIntegrityException(
                "Order.BasketCurrencyNull",
                "BasketSnapshot.Currency must not be null.");
        }

        var currency = basket.Currency;

        if (orderId == Guid.Empty)
        {
            throw new DataIntegrityException(
                "Order.OrderIdEmpty",
                "OrderId must not be empty.");
        }

        if (buyerId == Guid.Empty)
        {
            throw new DataIntegrityException(
                "Order.BuyerIdEmpty",
                "BuyerId must not be empty.");
        }

        if (paymentMethodId == Guid.Empty)
        {
            throw new DataIntegrityException(
                "Order.PaymentMethodIdEmpty",
                "PaymentMethodId must not be empty.");
        }

        // I-7 — at least one item.
        if (basket.Items.Count == 0)
        {
            throw new DataIntegrityException(
                "Order.BasketEmpty",
                "Cannot create an order from an empty basket.");
        }

        var items = new List<OrderItem>(basket.Items.Count);
        var totalAmount = 0m;

        foreach (var basketItem in basket.Items)
        {
            // I-8 — positive quantity + unit price.
            if (basketItem.Quantity <= 0)
            {
                throw new DataIntegrityException(
                    "Order.ItemQuantityNotPositive",
                    $"Basket item for product '{basketItem.ProductId}' has non-positive quantity {basketItem.Quantity}.");
            }

            if (basketItem.UnitPriceAmount <= 0)
            {
                throw new DataIntegrityException(
                    "Order.ItemUnitPriceNotPositive",
                    $"Basket item for product '{basketItem.ProductId}' has non-positive unit price {basketItem.UnitPriceAmount}.");
            }

            var snapshotResult = ProductSnapshot.Create(basketItem.Sku, basketItem.Name);
            if (snapshotResult.IsFailed)
            {
                throw new DataIntegrityException(
                    "Order.InvalidProductSnapshot",
                    $"Basket item for product '{basketItem.ProductId}' has invalid snapshot: {FormatErrors(snapshotResult)}.");
            }

            // Money.Create is permissive post-School-B; .Value is safe (currency is non-null).
            // Positivity of unitPrice is enforced by the guard above + OrderItem.Create.
            var unitPrice = Money.Create(basketItem.UnitPriceAmount, currency).Value;

            var itemResult = OrderItem.Create(
                basketItem.ProductId,
                snapshotResult.Value,
                basketItem.Quantity,
                unitPrice);
            if (itemResult.IsFailed)
            {
                throw new DataIntegrityException(
                    "Order.InvalidOrderItem",
                    $"Basket item for product '{basketItem.ProductId}' is invalid: {FormatErrors(itemResult)}.");
            }

            items.Add(itemResult.Value);
            totalAmount += itemResult.Value.LineTotal.Amount;
        }

        // I-9 is naturally enforced — every item is constructed with currency. Money.Create
        // is permissive post-School-B (currency-null check only); the .Value access is safe
        // because currency is non-null here (validated by the guard at the top of this method).
        var total = Money.Create(totalAmount, currency).Value;

        var order = new Order
        {
            Id = orderId,
            BuyerId = buyerId,
            PaymentMethodId = paymentMethodId,
            ShippingAddress = shippingAddress,
            BillingAddress = billingAddress,
            Status = OrderStatus.Created,
            Total = total,
            CreatedAtUtc = utcNow,
        };
        order._items.AddRange(items);

        order.AddDomainEvent(new OrderCreatedDomainEvent
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            PaymentMethodId = order.PaymentMethodId,
            Items = items.Select(i => new OrderCreatedDomainEventItem(
                i.ProductId,
                i.ProductSnapshot.Sku,
                i.ProductSnapshot.Name,
                i.Quantity,
                i.UnitPrice.Amount,
                i.LineTotal.Amount)).ToList(),
            ShippingAddress = shippingAddress,
            BillingAddress = billingAddress,
            Total = total,
            CreatedAtUtc = utcNow,
            OccurredOnUtc = utcNow,
        });

        return order;
    }

    /// <summary>
    /// Saga-issued transition to <see cref="OrderStatus.StockReserved"/>.
    /// FSM-violation is bug-class (saga ordering is a system invariant).
    /// </summary>
    public Result MarkStockReserved(Guid reservationId, DateTimeOffset utcNow)
    {
        GuardTransition(OrderStatus.StockReserved);

        if (reservationId == Guid.Empty)
        {
            throw new DataIntegrityException(
                "Order.ReservationIdEmpty",
                "ReservationId must not be empty.");
        }

        Status = OrderStatus.StockReserved;
        StockReservationId = reservationId;
        StockReservedAtUtc = utcNow;

        AddDomainEvent(new OrderStockReservedDomainEvent
        {
            OrderId = Id,
            ReservationId = reservationId,
            OccurredOnUtc = utcNow,
        });

        return Result.Ok();
    }

    /// <summary>
    /// Saga-issued transition to <see cref="OrderStatus.PaymentCompleted"/>.
    /// </summary>
    public Result MarkPaymentCompleted(Guid paymentTransactionId, DateTimeOffset utcNow)
    {
        GuardTransition(OrderStatus.PaymentCompleted);

        if (paymentTransactionId == Guid.Empty)
        {
            throw new DataIntegrityException(
                "Order.PaymentTransactionIdEmpty",
                "PaymentTransactionId must not be empty.");
        }

        Status = OrderStatus.PaymentCompleted;
        PaymentTransactionId = paymentTransactionId;
        PaymentCompletedAtUtc = utcNow;

        AddDomainEvent(new OrderPaymentCompletedDomainEvent
        {
            OrderId = Id,
            PaymentTransactionId = paymentTransactionId,
            OccurredOnUtc = utcNow,
        });

        return Result.Ok();
    }

    /// <summary>
    /// Saga-issued transition to <see cref="OrderStatus.Confirmed"/> — the
    /// fire-and-forget terminal-happy marker after stock + payment are green.
    /// </summary>
    public Result Confirm(DateTimeOffset utcNow)
    {
        GuardTransition(OrderStatus.Confirmed);

        Status = OrderStatus.Confirmed;
        ConfirmedAtUtc = utcNow;

        AddDomainEvent(new OrderConfirmedDomainEvent
        {
            OrderId = Id,
            BuyerId = BuyerId,
            Items = _items.ToList(),
            Total = Total,
            BillingAddress = BillingAddress,
            ConfirmedAtUtc = utcNow,
            OccurredOnUtc = utcNow,
        });

        return Result.Ok();
    }

    /// <summary>
    /// Admin/warehouse transition to <see cref="OrderStatus.Shipped"/>. Carrier
    /// and tracking-number validation failure is bug-class — the admin UI is
    /// expected to pre-validate the shape (<c>ordering.md § 9.3</c>).
    /// </summary>
    public Result MarkShipped(string carrier, string trackingNumber, DateTimeOffset utcNow)
    {
        GuardTransition(OrderStatus.Shipped);

        var shipmentResult = ShipmentInfo.Create(carrier, trackingNumber, utcNow);
        if (shipmentResult.IsFailed)
        {
            throw new DataIntegrityException(
                "Order.InvalidShipmentInfo",
                $"ShipmentInfo is invalid: {FormatErrors(shipmentResult)}.");
        }

        Status = OrderStatus.Shipped;
        Shipment = shipmentResult.Value;

        AddDomainEvent(new OrderShippedDomainEvent
        {
            OrderId = Id,
            BuyerId = BuyerId,
            Carrier = shipmentResult.Value.Carrier,
            TrackingNumber = shipmentResult.Value.TrackingNumber,
            ShippedAtUtc = shipmentResult.Value.ShippedAtUtc,
            OccurredOnUtc = utcNow,
        });

        return Result.Ok();
    }

    /// <summary>
    /// Admin/dev (v1) transition to <see cref="OrderStatus.Delivered"/> — the
    /// happy-path terminal state.
    /// </summary>
    public Result MarkDelivered(DateTimeOffset utcNow)
    {
        GuardTransition(OrderStatus.Delivered);

        Status = OrderStatus.Delivered;
        DeliveredAtUtc = utcNow;

        AddDomainEvent(new OrderDeliveredDomainEvent
        {
            OrderId = Id,
            BuyerId = BuyerId,
            DeliveredAtUtc = utcNow,
            OccurredOnUtc = utcNow,
        });

        return Result.Ok();
    }

    /// <summary>
    /// Cancellation request from buyer or admin. The ONLY user-visible error
    /// in the saga flow — cancellation from <see cref="OrderStatus.Shipped"/>
    /// / <see cref="OrderStatus.Delivered"/> / terminal returns
    /// <see cref="OrderingErrors.CannotCancelInStatus"/> (I-12). All other
    /// transition violations in this aggregate are bug-class.
    /// </summary>
    /// <remarks>
    /// Reason-shape failures (empty / too-long) reaching this method are
    /// bug-class — the HTTP/BFF surface is expected to pre-validate.
    /// </remarks>
    public Result Cancel(string reason, DateTimeOffset utcNow)
    {
        if (!Status.CanTransitionTo(OrderStatus.Cancelled))
        {
            return Result.Fail(OrderingErrors.CannotCancelInStatus(Status.Name));
        }

        var cancellationResult = CancellationInfo.Create(reason, Status, utcNow);
        if (cancellationResult.IsFailed)
        {
            throw new DataIntegrityException(
                "Order.InvalidCancellationInfo",
                $"CancellationInfo is invalid: {FormatErrors(cancellationResult)}.");
        }

        var previousStatus = Status;
        Status = OrderStatus.Cancelled;
        Cancellation = cancellationResult.Value;

        AddDomainEvent(new OrderCancelledDomainEvent
        {
            OrderId = Id,
            BuyerId = BuyerId,
            Reason = cancellationResult.Value.Reason,
            AtStatus = previousStatus.Name,
            CancelledAtUtc = utcNow,
            Items = _items.ToList(),
            Total = Total,
            BillingAddress = BillingAddress,
            OccurredOnUtc = utcNow,
        });

        return Result.Ok();
    }

    /// <summary>
    /// Saga-issued transition to <see cref="OrderStatus.Failed"/>. Reachable
    /// from <c>Created</c>, <c>StockReserved</c>, and <c>PaymentCompleted</c>
    /// only — NOT from <c>Confirmed</c> (by then both stock and payment are
    /// green; see <c>example-mapping/ordering.md Session 1 R4</c>).
    /// </summary>
    public Result Fail(string errorCode, string errorMessage, DateTimeOffset utcNow)
    {
        GuardTransition(OrderStatus.Failed);

        var failureResult = FailureInfo.Create(errorCode, errorMessage, Status, utcNow);
        if (failureResult.IsFailed)
        {
            throw new DataIntegrityException(
                "Order.InvalidFailureInfo",
                $"FailureInfo is invalid: {FormatErrors(failureResult)}.");
        }

        var previousStatus = Status;
        Status = OrderStatus.Failed;
        Failure = failureResult.Value;

        AddDomainEvent(new OrderFailedDomainEvent
        {
            OrderId = Id,
            BuyerId = BuyerId,
            ErrorCode = failureResult.Value.ErrorCode,
            ErrorMessage = failureResult.Value.ErrorMessage,
            AtStatus = previousStatus.Name,
            FailedAtUtc = utcNow,
            OccurredOnUtc = utcNow,
        });

        return Result.Ok();
    }

    private void GuardTransition(OrderStatus target)
    {
        if (!Status.CanTransitionTo(target))
        {
            throw new DataIntegrityException(
                "Order.InvalidStatusTransition",
                $"Cannot transition order '{Id}' from '{Status.Name}' to '{target.Name}'.");
        }
    }

    private static string FormatErrors(ResultBase result) =>
        string.Join("; ", result.Errors.Select(e => e.Message));
}
