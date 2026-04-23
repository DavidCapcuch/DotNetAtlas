using System.Collections.Immutable;
using Basket.Domain.Baskets.Errors;
using Basket.Domain.Baskets.Events;
using Basket.Domain.Baskets.ValueObjects;
using FluentResults;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Exceptions;
using Platform.SharedKernel.ValueObjects;

namespace Basket.Domain.Baskets;

/// <summary>
/// Aggregate root — the ephemeral, per-user shopping session for the technical
/// Basket bounded context. Lives in Redis (primary store) with a SQL side-car
/// for the outbox only (see ADR-0003). Identity is the user's <see cref="Guid"/>
/// because each user has exactly one basket at a time and this drives the Redis
/// key (<c>basket:{userId}</c>).
/// </summary>
/// <remarks>
/// Invariants enforced here (basket.md § 10):
/// <list type="number">
/// <item><c>UserId != Guid.Empty</c> and immutable.</item>
/// <item>Every item has <c>Quantity &gt;= 1</c>.</item>
/// <item><c>Items.Count &lt;= 50</c> (distinct products).</item>
/// <item>No duplicate <c>ProductId</c> — duplicate adds collapse into a quantity bump.</item>
/// <item>All items share a single currency.</item>
/// <item>Snapshots are immutable until <see cref="RefreshPrices"/> replaces them wholesale.</item>
/// <item>Empty baskets cannot be checked out.</item>
/// <item><see cref="Version"/> is strictly monotonic — every successful mutation increments it.</item>
/// </list>
/// Time is injected via <c>DateTimeOffset utcNow</c> on every mutating method
/// (ADR-0015) — the domain never reads <c>DateTimeOffset.UtcNow</c> directly.
/// </remarks>
public sealed class Basket : AggregateRoot<Guid>
{
    /// <summary>Upper bound on distinct products in a basket (basket.md invariant 3).</summary>
    public const int MaxItems = 50;

    private readonly List<BasketItem> _items = [];

    /// <summary>
    /// Identifier of the user who owns this basket. Same value as <see cref="Entity{TId}.Id"/>.
    /// </summary>
    public Guid UserId => Id;

    /// <summary>All line items currently in the basket.</summary>
    public IReadOnlyCollection<BasketItem> Items => _items;

    /// <summary>
    /// Optimistic-concurrency token incremented on every successful mutation.
    /// A freshly created basket is at <c>Version = 0</c> and becomes <c>1</c> after
    /// its first persisted change.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>Creation time, set once by <see cref="Create"/>.</summary>
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>
    /// Last mutation time. Refreshed on every successful mutation (also drives the
    /// Redis 30-day sliding TTL in the infrastructure layer).
    /// </summary>
    public DateTimeOffset LastModifiedAtUtc { get; private set; }

    /// <summary>
    /// Computed total — <c>Sum(item.Snapshot.Price * item.Quantity)</c> in the basket's
    /// (sole) currency. Empty basket returns null because we cannot know the currency
    /// without an item; callers aware of the invariant "empty basket cannot checkout"
    /// rely on that guard instead.
    /// </summary>
    public BasketTotal? Total
    {
        get
        {
            if (_items.Count == 0)
            {
                return null;
            }

            var currency = _items[0].Snapshot.Price.Currency;
            decimal sum = 0m;
            foreach (var item in _items)
            {
                sum += item.Snapshot.Price.Amount * item.Quantity;
            }

            return new BasketTotal(new Money(sum, currency));
        }
    }

    private Basket()
    {
    }

    /// <summary>
    /// Creates a new, empty basket for <paramref name="userId"/>. Raises
    /// <see cref="BasketCreatedDomainEvent"/>.
    /// </summary>
    /// <param name="userId">The user's identifier. Must not be <see cref="Guid.Empty"/>.</param>
    /// <param name="utcNow">The current UTC instant (inject via <c>TimeProvider</c>).</param>
    /// <exception cref="DataIntegrityException">Thrown when <paramref name="userId"/> is <see cref="Guid.Empty"/>.</exception>
    public static Basket Create(Guid userId, DateTimeOffset utcNow)
    {
        Throw.If(userId == Guid.Empty, new DataIntegrityException(
            "Basket.InvalidUserId",
            "Basket UserId must not be empty."));

        var basket = new Basket
        {
            Id = userId,
            Version = 0,
            CreatedAtUtc = utcNow,
            LastModifiedAtUtc = utcNow,
        };

        basket.AddDomainEvent(new BasketCreatedDomainEvent { UserId = userId });
        return basket;
    }

    /// <summary>
    /// Reconstitutes a previously-persisted basket from its stored state without
    /// raising <see cref="BasketCreatedDomainEvent"/>. Used exclusively by the
    /// persistence seam in <c>Basket.Infrastructure</c> (see <c>BasketStateMapper</c>);
    /// application code must use <see cref="Create"/>.
    /// </summary>
    /// <param name="userId">The basket owner's identifier.</param>
    /// <param name="version">The persisted <c>Version</c> token.</param>
    /// <param name="createdAtUtc">The original creation instant.</param>
    /// <param name="lastModifiedAtUtc">The instant of the last persisted mutation.</param>
    /// <param name="items">All items at the time of serialization.</param>
    /// <exception cref="DataIntegrityException">Thrown when <paramref name="userId"/> is <see cref="Guid.Empty"/>.</exception>
    internal static Basket Rehydrate(
        Guid userId,
        int version,
        DateTimeOffset createdAtUtc,
        DateTimeOffset lastModifiedAtUtc,
        IReadOnlyList<BasketItem> items)
    {
        Throw.If(userId == Guid.Empty, new DataIntegrityException(
            "Basket.InvalidUserId",
            "Basket UserId must not be empty."));

        ArgumentNullException.ThrowIfNull(items);

        var basket = new Basket
        {
            Id = userId,
            Version = version,
            CreatedAtUtc = createdAtUtc,
            LastModifiedAtUtc = lastModifiedAtUtc,
        };

        basket._items.AddRange(items);
        return basket;
    }

    /// <summary>
    /// Adds <paramref name="quantity"/> units of the given product to the basket.
    /// If the product is already present the quantities collapse into a single line.
    /// Raises <see cref="ItemAddedToBasketDomainEvent"/>.
    /// </summary>
    /// <returns>
    /// <see cref="Result.Ok()"/> on success; otherwise
    /// <see cref="BasketErrors.InvalidQuantity"/>,
    /// <see cref="BasketErrors.MaxItemsReached"/>, or
    /// <see cref="BasketErrors.CurrencyMismatch"/>.
    /// </returns>
    public Result AddItem(Guid productId, ProductSnapshot snapshot, int quantity, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (quantity < 1)
        {
            return Result.Fail(BasketErrors.InvalidQuantity());
        }

        var existing = FindItem(productId);

        Money capturedPriceForEvent;

        if (existing is null)
        {
            if (_items.Count >= MaxItems)
            {
                return Result.Fail(BasketErrors.MaxItemsReached(MaxItems));
            }

            if (_items.Count > 0 && _items[0].Snapshot.Price.Currency != snapshot.Price.Currency)
            {
                return Result.Fail(BasketErrors.CurrencyMismatch());
            }

            _items.Add(new BasketItem(productId, snapshot, quantity));
            capturedPriceForEvent = snapshot.Price;
        }
        else
        {
            // Currency must still match the basket's — the existing line is already in
            // the basket's currency, but a new snapshot could arrive in a different one.
            if (existing.Snapshot.Price.Currency != snapshot.Price.Currency)
            {
                return Result.Fail(BasketErrors.CurrencyMismatch());
            }

            // Preserve the frozen snapshot (invariant 6) — the newly arriving snapshot is
            // ignored on a quantity bump. The event broadcasts the FROZEN price so that
            // consumers (logs, metrics, future projections) never see a price the basket
            // did not actually commit to.
            var index = _items.IndexOf(existing);
            _items[index] = new BasketItem(productId, existing.Snapshot, existing.Quantity + quantity);
            capturedPriceForEvent = existing.Snapshot.Price;
        }

        Touch(utcNow);
        AddDomainEvent(new ItemAddedToBasketDomainEvent
        {
            UserId = Id,
            ProductId = productId,
            Quantity = quantity,
            CapturedPrice = capturedPriceForEvent,
        });
        return Result.Ok();
    }

    /// <summary>
    /// Removes the line for <paramref name="productId"/> from the basket. Idempotent —
    /// if the product is not present the call returns <see cref="Result.Ok()"/> and
    /// does not raise an event, does not change <see cref="Version"/>, does not
    /// refresh <see cref="LastModifiedAtUtc"/>.
    /// </summary>
    public Result RemoveItem(Guid productId, DateTimeOffset utcNow)
    {
        var existing = FindItem(productId);
        if (existing is null)
        {
            return Result.Ok();
        }

        _items.Remove(existing);
        Touch(utcNow);
        AddDomainEvent(new ItemRemovedFromBasketDomainEvent
        {
            UserId = Id,
            ProductId = productId,
        });
        return Result.Ok();
    }

    /// <summary>
    /// Replaces the quantity of the line for <paramref name="productId"/>. Fails if
    /// the product is not present or the new quantity is less than 1. If the new
    /// quantity equals the existing quantity the call is a no-op (no event, no
    /// version bump).
    /// </summary>
    public Result ChangeQuantity(Guid productId, int newQuantity, DateTimeOffset utcNow)
    {
        if (newQuantity < 1)
        {
            return Result.Fail(BasketErrors.InvalidQuantity());
        }

        var existing = FindItem(productId);
        if (existing is null)
        {
            return Result.Fail(BasketErrors.ItemNotFound(productId));
        }

        if (existing.Quantity == newQuantity)
        {
            return Result.Ok();
        }

        var index = _items.IndexOf(existing);
        _items[index] = new BasketItem(productId, existing.Snapshot, newQuantity);
        Touch(utcNow);
        AddDomainEvent(new ItemQuantityChangedDomainEvent
        {
            UserId = Id,
            ProductId = productId,
            OldQuantity = existing.Quantity,
            NewQuantity = newQuantity,
        });
        return Result.Ok();
    }

    /// <summary>
    /// Replaces snapshots for every product in <paramref name="freshSnapshots"/> whose
    /// id matches an existing line, preserving quantities. Items in the basket whose
    /// id is absent from the input are left untouched. Items in the input whose id is
    /// not in the basket are silently dropped. Raises
    /// <see cref="BasketPricesRefreshedDomainEvent"/> listing only items whose
    /// snapshot price actually changed; if no prices changed nothing is emitted and
    /// version/timestamp do not advance.
    /// </summary>
    /// <exception cref="DataIntegrityException">
    /// Thrown if <paramref name="freshSnapshots"/> introduces a currency different from
    /// the basket's — that would violate invariant 5 and indicates a caller bug
    /// (refresh came from a different catalog scope than the original add).
    /// </exception>
    public Result RefreshPrices(
        IReadOnlyList<(Guid ProductId, ProductSnapshot Snapshot)> freshSnapshots,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(freshSnapshots);

        if (_items.Count == 0 || freshSnapshots.Count == 0)
        {
            return Result.Ok();
        }

        var basketCurrency = _items[0].Snapshot.Price.Currency;
        var changes = new List<PriceChange>();

        foreach (var (productId, newSnapshot) in freshSnapshots)
        {
            var existing = FindItem(productId);
            if (existing is null)
            {
                continue;
            }

            Throw.If(newSnapshot.Price.Currency != basketCurrency, new DataIntegrityException(
                "Basket.RefreshCurrencyMismatch",
                $"Refresh snapshot currency '{newSnapshot.Price.Currency.Name}' does not match basket currency '{basketCurrency.Name}'."));

            if (existing.Snapshot.Price == newSnapshot.Price)
            {
                // Price did not change — still refresh Sku/Name/CapturedAt to reflect the
                // latest catalog state, but do not list it as a price change.
                var index = _items.IndexOf(existing);
                _items[index] = new BasketItem(productId, newSnapshot, existing.Quantity);
                continue;
            }

            changes.Add(new PriceChange(productId, existing.Snapshot.Price, newSnapshot.Price));
            var idx = _items.IndexOf(existing);
            _items[idx] = new BasketItem(productId, newSnapshot, existing.Quantity);
        }

        if (changes.Count == 0)
        {
            return Result.Ok();
        }

        Touch(utcNow);
        AddDomainEvent(new BasketPricesRefreshedDomainEvent
        {
            UserId = Id,
            Changes = changes,
        });
        return Result.Ok();
    }

    /// <summary>
    /// Removes every line from the basket and raises <see cref="BasketClearedDomainEvent"/>.
    /// The basket remains reachable — only <see cref="Checkout"/> deletes the entry.
    /// If the basket is already empty the call is a no-op.
    /// </summary>
    public void Clear(DateTimeOffset utcNow)
    {
        if (_items.Count == 0)
        {
            return;
        }

        _items.Clear();
        Touch(utcNow);
        AddDomainEvent(new BasketClearedDomainEvent { UserId = Id });
    }

    /// <summary>
    /// Terminal transition — captures a full <see cref="BasketSnapshot"/>, raises
    /// <see cref="BasketCheckedOutDomainEvent"/>, and relies on an in-process handler
    /// (milestone M4) to transform it into the external Avro event and write the
    /// outbox. Fails with <see cref="BasketErrors.EmptyBasket"/> on an empty basket.
    /// </summary>
    /// <param name="correlationId">
    /// Correlation id generated by the caller (API layer via <c>Guid.CreateVersion7()</c>).
    /// Becomes the Checkout Saga's correlation id. Must not be <see cref="Guid.Empty"/>.
    /// </param>
    /// <param name="shippingAddress">
    /// Courier field — shipping address from the command. Basket does not own it
    /// ([ADR-0005](../../../../docs/adr/0005-customer-data-in-ordering.md)); it ferries
    /// the value to the outbox publisher via the raised domain event.
    /// </param>
    /// <param name="billingAddress">Courier field — billing address; may equal <paramref name="shippingAddress"/>.</param>
    /// <param name="paymentMethodId">
    /// Courier field — saved-payment-method reference owned by Payments. Must not be <see cref="Guid.Empty"/>.
    /// </param>
    /// <param name="utcNow">Current UTC instant.</param>
    public Result Checkout(
        Guid correlationId,
        Address shippingAddress,
        Address billingAddress,
        Guid paymentMethodId,
        DateTimeOffset utcNow)
    {
        Throw.If(correlationId == Guid.Empty, new DataIntegrityException(
            "Basket.InvalidCorrelationId",
            "CorrelationId must not be empty."));
        ArgumentNullException.ThrowIfNull(shippingAddress);
        ArgumentNullException.ThrowIfNull(billingAddress);
        Throw.If(paymentMethodId == Guid.Empty, new DataIntegrityException(
            "Basket.InvalidPaymentMethodId",
            "PaymentMethodId must not be empty."));

        if (_items.Count == 0)
        {
            return Result.Fail(BasketErrors.EmptyBasket());
        }

        var snapshot = new BasketSnapshot(_items.ToImmutableArray(), Total!);
        Touch(utcNow);
        AddDomainEvent(new BasketCheckedOutDomainEvent
        {
            UserId = Id,
            CorrelationId = correlationId,
            Snapshot = snapshot,
            ShippingAddress = shippingAddress,
            BillingAddress = billingAddress,
            PaymentMethodId = paymentMethodId,
        });
        return Result.Ok();
    }

    private BasketItem? FindItem(Guid productId)
    {
        foreach (var item in _items)
        {
            if (item.ProductId == productId)
            {
                return item;
            }
        }

        return null;
    }

    private void Touch(DateTimeOffset utcNow)
    {
        Version++;
        LastModifiedAtUtc = utcNow;
    }
}
