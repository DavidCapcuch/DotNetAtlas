using DotNetAtlas.SharedKernel.Base;
using DotNetAtlas.SharedKernel.Exceptions;
using Ordering.Domain.AlertSubscriptionOrders.Events;
using Ordering.Domain.ValueObjects;

namespace Ordering.Domain.AlertSubscriptionOrders;

/// <summary>
/// Aggregate root representing a subscription order for alert subscription purchase or extension.
/// Tracks the order lifecycle from initiation through completion or failure.
/// </summary>
/// <remarks>
/// This aggregate can raise the following domain events:
/// <list type="bullet">
/// <item><see cref="AlertSubscriptionPurchaseOrderCreatedDomainEvent"/>: When a new purchase order is created.</item>
/// <item><see cref="AlertSubscriptionExtensionOrderCreatedDomainEvent"/>: When a new extension order is created.</item>
/// <item><see cref="AlertSubscriptionOrderCompletedDomainEvent"/>: When the order is completed successfully.</item>
/// <item><see cref="AlertSubscriptionOrderFailedDomainEvent"/>: When the order fails.</item>
/// </list>
/// </remarks>
public sealed class AlertSubscriptionOrder : AggregateRoot<Guid>, IAuditableEntity
{
    /// <summary>
    /// User who initiated the order.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Type of order: Purchase or Extension.
    /// </summary>
    public AlertSubscriptionOrderType AlertSubscriptionOrderType { get; private set; }

    /// <summary>
    /// ID of the saved payment method to use.
    /// </summary>
    public Guid PaymentMethodId { get; private set; }

    /// <summary>
    /// Subscription tier (only for purchases; null for extensions).
    /// </summary>
    public AlertSubscriptionTier? Tier { get; private set; }

    /// <summary>
    /// Duration of the subscription in days.
    /// </summary>
    public int DurationDays { get; private set; }

    /// <summary>
    /// The price of the subscription order.
    /// </summary>
    public Money Price { get; private set; } = null!;

    /// <summary>
    /// Current status of the order.
    /// </summary>
    public AlertSubscriptionOrderStatus Status { get; private set; } = null!;

    /// <summary>
    /// UTC timestamp when the order was created.
    /// </summary>
    public DateTime CreatedAtUtc { get; private set; }

    private AlertSubscriptionOrder()
    {
    }

    /// <summary>
    /// Creates a new purchase order for an alert subscription.
    /// </summary>
    /// <param name="userId">The user initiating the purchase.</param>
    /// <param name="paymentMethodId">ID of the saved payment method.</param>
    /// <param name="tier">The subscription tier being purchased (Pro or Ultra).</param>
    /// <param name="durationDays">Duration of the subscription in days.</param>
    /// <param name="price">The payment amount and currency.</param>
    /// <returns>A new purchase order instance.</returns>
    /// <exception cref="DataIntegrityException">Thrown when tier is Free or duration is invalid.</exception>
    /// <remarks>
    /// Possible raised events:
    /// <list type="bullet">
    /// <item><see cref="AlertSubscriptionPurchaseOrderCreatedDomainEvent"/>: Always raised.</item>
    /// </list>
    /// </remarks>
    public static AlertSubscriptionOrder CreatePurchaseOrder(
        Guid userId,
        Guid paymentMethodId,
        AlertSubscriptionTier tier,
        int durationDays,
        Money price)
    {
        Throw.If(tier == AlertSubscriptionTier.Free, new DataIntegrityException(
            "AlertSubscriptionOrder.CannotPurchaseFreeTier",
            "Cannot create a purchase order for the Free tier."));

        Throw.If(durationDays <= 0, new DataIntegrityException(
            "AlertSubscriptionOrder.InvalidDuration",
            "Subscription duration must be greater than zero."));

        var purchaseOrder = new AlertSubscriptionOrder
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            AlertSubscriptionOrderType = AlertSubscriptionOrderType.Purchase,
            PaymentMethodId = paymentMethodId,
            Tier = tier,
            DurationDays = durationDays,
            Price = price,
            Status = AlertSubscriptionOrderStatus.Initiated
        };

        purchaseOrder.AddDomainEvent(new AlertSubscriptionPurchaseOrderCreatedDomainEvent
        {
            AlertSubscriptionOrderId = purchaseOrder.Id,
            UserId = userId,
            PaymentMethodId = paymentMethodId,
            Tier = tier,
            DurationDays = durationDays,
            Price = price
        });

        return purchaseOrder;
    }

    /// <summary>
    /// Creates a new extension order for an existing alert subscription.
    /// </summary>
    /// <param name="userId">The user initiating the extension.</param>
    /// <param name="paymentMethodId">ID of the saved payment method.</param>
    /// <param name="durationDays">Duration to extend the subscription in days.</param>
    /// <param name="price">The payment amount and currency.</param>
    /// <returns>A new extension order instance.</returns>
    /// <exception cref="DataIntegrityException">Thrown when duration is invalid.</exception>
    /// <remarks>
    /// Possible raised events:
    /// <list type="bullet">
    /// <item><see cref="AlertSubscriptionExtensionOrderCreatedDomainEvent"/>: Always raised.</item>
    /// </list>
    /// </remarks>
    public static AlertSubscriptionOrder CreateExtensionOrder(
        Guid userId,
        Guid paymentMethodId,
        int durationDays,
        Money price)
    {
        Throw.If(durationDays <= 0, new DataIntegrityException(
            "AlertSubscriptionOrder.InvalidDuration",
            "Subscription duration must be greater than zero."));

        var extensionOrder = new AlertSubscriptionOrder
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            AlertSubscriptionOrderType = AlertSubscriptionOrderType.Extension,
            PaymentMethodId = paymentMethodId,
            Tier = null,
            DurationDays = durationDays,
            Price = price,
            Status = AlertSubscriptionOrderStatus.Initiated
        };

        extensionOrder.AddDomainEvent(new AlertSubscriptionExtensionOrderCreatedDomainEvent
        {
            AlertSubscriptionOrderId = extensionOrder.Id,
            UserId = userId,
            PaymentMethodId = paymentMethodId,
            DurationDays = durationDays,
            Price = price
        });

        return extensionOrder;
    }

    /// <summary>
    /// Transitions the order to the Completed status.
    /// </summary>
    /// <exception cref="DataIntegrityException">Thrown when the current status cannot transition to Completed.</exception>
    /// <remarks>
    /// Possible raised events:
    /// <list type="bullet">
    /// <item><see cref="AlertSubscriptionOrderCompletedDomainEvent"/>: Always raised.</item>
    /// </list>
    /// </remarks>
    public void Complete()
    {
        Throw.If(!Status.CanTransitionTo(AlertSubscriptionOrderStatus.Completed), new DataIntegrityException(
            "AlertSubscriptionOrder.InvalidStatusTransition",
            $"Cannot transition from '{Status.Name}' to '{AlertSubscriptionOrderStatus.Completed.Name}'."));

        Status = AlertSubscriptionOrderStatus.Completed;

        AddDomainEvent(new AlertSubscriptionOrderCompletedDomainEvent
        {
            AlertSubscriptionOrderId = Id
        });
    }

    /// <summary>
    /// Transitions the order to the Failed status.
    /// </summary>
    /// <exception cref="DataIntegrityException">Thrown when the current status cannot transition to Failed.</exception>
    /// <remarks>
    /// Possible raised events:
    /// <list type="bullet">
    /// <item><see cref="AlertSubscriptionOrderFailedDomainEvent"/>: Always raised.</item>
    /// </list>
    /// </remarks>
    public void Fail()
    {
        Throw.If(!Status.CanTransitionTo(AlertSubscriptionOrderStatus.Failed), new DataIntegrityException(
            "AlertSubscriptionOrder.InvalidStatusTransition",
            $"Cannot transition from '{Status.Name}' to '{AlertSubscriptionOrderStatus.Failed.Name}'."));

        Status = AlertSubscriptionOrderStatus.Failed;

        AddDomainEvent(new AlertSubscriptionOrderFailedDomainEvent
        {
            AlertSubscriptionOrderId = Id
        });
    }

    public DateTimeOffset CreatedUtc { get; private set; }
    public DateTimeOffset LastModifiedUtc { get; private set; }
}
