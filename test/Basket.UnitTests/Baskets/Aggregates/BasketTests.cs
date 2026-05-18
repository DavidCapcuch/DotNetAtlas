using Basket.Domain.Baskets;
using Basket.Domain.Baskets.Events;
using Basket.Domain.Baskets.ValueObjects;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Platform.SharedKernel.Errors;
using Platform.SharedKernel.Exceptions;
using Platform.SharedKernel.ValueObjects;
using BasketAggregate = Basket.Domain.Baskets.Basket;

namespace Basket.UnitTests.Baskets.Aggregates;

/// <summary>
/// Covers every invariant and every mutating method of the Basket aggregate root.
/// Time is injected via <see cref="FakeTimeProvider"/> for deterministic
/// <c>LastModifiedAtUtc</c> assertions (ADR-0015).
/// </summary>
public class BasketTests
{
    private readonly FakeTimeProvider _fakeTimeProvider = new();

    private DateTimeOffset UtcNow => _fakeTimeProvider.GetUtcNow();

    // ------------------------------------------------------------------
    // Create
    // ------------------------------------------------------------------

    [Fact]
    public void Create_WhenValidUserId_ReturnsEmptyBasketAndRaisesBasketCreatedEvent()
    {
        var userId = Guid.CreateVersion7();

        var basket = BasketAggregate.Create(userId, UtcNow);

        using (new AssertionScope())
        {
            basket.UserId.Should().Be(userId);
            basket.Id.Should().Be(userId);
            basket.Items.Should().BeEmpty();
            basket.Version.Should().Be(0);
            basket.CreatedAtUtc.Should().Be(UtcNow);
            basket.LastModifiedAtUtc.Should().Be(UtcNow);
            basket.Total.Should().BeNull();
            basket.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<BasketCreatedDomainEvent>()
                .Which.UserId.Should().Be(userId);
        }
    }

    [Fact]
    public void Create_WhenEmptyUserId_ThrowsDataIntegrityException()
    {
        var act = () => BasketAggregate.Create(Guid.Empty, UtcNow);

        act.Should().Throw<DataIntegrityException>()
            .WithMessage("*UserId*");
    }

    // ------------------------------------------------------------------
    // AddItem — invariants 2 (qty >=1), 3 (max 50), 4 (dedupe), 5 (currency)
    // ------------------------------------------------------------------

    [Fact]
    public void AddItem_WhenFirstItem_AppendsLineAndRaisesItemAddedEventAndIncrementsVersion()
    {
        var basket = NewEmptyBasket();
        _ = basket.PopDomainEvents();
        var productId = Guid.CreateVersion7();
        var snapshot = BasketTestData.Snapshot();
        _fakeTimeProvider.Advance(TimeSpan.FromMinutes(1));
        var utcAtAdd = UtcNow;

        var result = basket.AddItem(productId, snapshot, quantity: 2, utcAtAdd);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            basket.Items.Should().ContainSingle();
            var line = basket.Items.Single();
            line.ProductId.Should().Be(productId);
            line.Snapshot.Should().Be(snapshot);
            line.Quantity.Should().Be(2);
            basket.Version.Should().Be(1);
            basket.LastModifiedAtUtc.Should().Be(utcAtAdd);
            var evt = basket.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<ItemAddedToBasketDomainEvent>()
                .Subject;
            evt.ProductId.Should().Be(productId);
            evt.Quantity.Should().Be(2);
            evt.CapturedPrice.Should().Be(snapshot.Price);
        }
    }

    [Fact]
    public void AddItem_WhenProductAlreadyPresent_CollapsesIntoQuantityBumpAndEmitsDeltaQuantity()
    {
        var basket = NewEmptyBasket();
        var productId = Guid.CreateVersion7();
        var snapshot = BasketTestData.Snapshot();
        basket.AddItem(productId, snapshot, 2, UtcNow);
        _ = basket.PopDomainEvents();
        var versionBefore = basket.Version;

        var result = basket.AddItem(productId, snapshot, quantity: 3, UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            basket.Items.Should().ContainSingle();
            basket.Items.Single().Quantity.Should().Be(5);
            basket.Version.Should().Be(versionBefore + 1);
            var evt = basket.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<ItemAddedToBasketDomainEvent>()
                .Subject;
            evt.Quantity.Should().Be(3);
        }
    }

    [Fact]
    public void AddItem_QuantityBumpWithDifferentSnapshotPrice_PreservesFrozenPriceAndBroadcastsIt()
    {
        // Regression: event and state must agree on which price the basket actually holds.
        // The arriving snapshot's price is ignored (invariant 6 — snapshots are immutable
        // until RefreshPrices replaces them wholesale). The event MUST broadcast the frozen
        // price so consumers never see a price the basket did not commit to.
        var basket = NewEmptyBasket();
        var productId = Guid.CreateVersion7();
        basket.AddItem(productId, BasketTestData.Snapshot(amount: 10m), 1, UtcNow);
        _ = basket.PopDomainEvents();

        var result = basket.AddItem(productId, BasketTestData.Snapshot(amount: 99m), 2, UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            basket.Items.Single().Snapshot.Price.Amount.Should().Be(10m);
            var evt = basket.PopDomainEvents().OfType<ItemAddedToBasketDomainEvent>().Single();
            evt.CapturedPrice.Amount.Should().Be(10m);
        }
    }

    [Fact]
    public void AddItem_AtMaxItemsMinusOne_Succeeds_PinningBoundary()
    {
        var basket = NewEmptyBasket();
        for (var i = 0; i < BasketAggregate.MaxItems - 1; i++)
        {
            basket.AddItem(Guid.CreateVersion7(), BasketTestData.Snapshot(sku: $"SKU-{i}"), 1, UtcNow);
        }

        var result = basket.AddItem(
            Guid.CreateVersion7(),
            BasketTestData.Snapshot(sku: "SKU-LAST"),
            1,
            UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            basket.Items.Should().HaveCount(BasketAggregate.MaxItems);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddItem_WhenQuantityLessThanOne_FailsInvalidQuantityAndLeavesStateUnchanged(int quantity)
    {
        var basket = NewEmptyBasket();
        _ = basket.PopDomainEvents();
        var versionBefore = basket.Version;

        var result = basket.AddItem(Guid.CreateVersion7(), BasketTestData.Snapshot(), quantity, UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            ErrorCodeOf(result).Should().Be("Basket.InvalidQuantity");
            basket.Items.Should().BeEmpty();
            basket.Version.Should().Be(versionBefore);
            basket.PopDomainEvents().Should().BeEmpty();
        }
    }

    [Fact]
    public void AddItem_WhenMaxItemsReached_FailsMaxItemsReached()
    {
        var basket = NewEmptyBasket();
        for (var i = 0; i < BasketAggregate.MaxItems; i++)
        {
            basket.AddItem(Guid.CreateVersion7(), BasketTestData.Snapshot(sku: $"SKU-{i}"), 1, UtcNow);
        }

        _ = basket.PopDomainEvents();
        var versionBefore = basket.Version;

        var result = basket.AddItem(Guid.CreateVersion7(), BasketTestData.Snapshot(sku: "SKU-X"), 1, UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            ErrorCodeOf(result).Should().Be("Basket.MaxItemsReached");
            basket.Items.Should().HaveCount(BasketAggregate.MaxItems);
            basket.Version.Should().Be(versionBefore);
            basket.PopDomainEvents().Should().BeEmpty();
        }
    }

    [Fact]
    public void AddItem_QuantityBumpOnExistingLine_DoesNotCountAgainstMaxItems()
    {
        var basket = NewEmptyBasket();
        var productId = Guid.CreateVersion7();
        var snapshot = BasketTestData.Snapshot();

        // Fill to MaxItems - 1 with unique products, then bump the same productId 3 times.
        for (var i = 0; i < BasketAggregate.MaxItems - 1; i++)
        {
            basket.AddItem(Guid.CreateVersion7(), BasketTestData.Snapshot(sku: $"SKU-{i}"), 1, UtcNow);
        }

        basket.AddItem(productId, snapshot, 1, UtcNow);
        var result1 = basket.AddItem(productId, snapshot, 1, UtcNow);
        var result2 = basket.AddItem(productId, snapshot, 1, UtcNow);

        using (new AssertionScope())
        {
            result1.Should().BeSuccess();
            result2.Should().BeSuccess();
            basket.Items.Should().HaveCount(BasketAggregate.MaxItems);
        }
    }

    [Fact]
    public void AddItem_WhenCurrencyDiffersFromBasket_FailsCurrencyMismatch()
    {
        var basket = NewEmptyBasket();
        basket.AddItem(Guid.CreateVersion7(), BasketTestData.Snapshot(currency: CurrencyCode.Usd), 1, UtcNow);
        _ = basket.PopDomainEvents();
        var versionBefore = basket.Version;

        var result = basket.AddItem(
            Guid.CreateVersion7(),
            BasketTestData.Snapshot(currency: CurrencyCode.Eur, sku: "SKU-EUR"),
            1,
            UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            ErrorCodeOf(result).Should().Be("Basket.CurrencyMismatch");
            basket.Items.Should().ContainSingle();
            basket.Version.Should().Be(versionBefore);
            basket.PopDomainEvents().Should().BeEmpty();
        }
    }

    // ------------------------------------------------------------------
    // RemoveItem — idempotency
    // ------------------------------------------------------------------

    [Fact]
    public void RemoveItem_WhenPresent_RemovesAndRaisesItemRemovedEvent()
    {
        var basket = NewEmptyBasket();
        var productId = Guid.CreateVersion7();
        basket.AddItem(productId, BasketTestData.Snapshot(), 1, UtcNow);
        _ = basket.PopDomainEvents();
        var versionBefore = basket.Version;

        var result = basket.RemoveItem(productId, UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            basket.Items.Should().BeEmpty();
            basket.Version.Should().Be(versionBefore + 1);
            basket.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<ItemRemovedFromBasketDomainEvent>()
                .Which.ProductId.Should().Be(productId);
        }
    }

    [Fact]
    public void RemoveItem_WhenNotPresent_IsIdempotentNoop()
    {
        var basket = NewEmptyBasket();
        _ = basket.PopDomainEvents();
        var versionBefore = basket.Version;
        var lastModifiedBefore = basket.LastModifiedAtUtc;

        _fakeTimeProvider.Advance(TimeSpan.FromMinutes(10));
        var result = basket.RemoveItem(Guid.CreateVersion7(), UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            basket.Version.Should().Be(versionBefore);
            basket.LastModifiedAtUtc.Should().Be(lastModifiedBefore);
            basket.PopDomainEvents().Should().BeEmpty();
        }
    }

    // ------------------------------------------------------------------
    // ChangeQuantity
    // ------------------------------------------------------------------

    [Fact]
    public void ChangeQuantity_WhenValid_UpdatesQuantityAndRaisesEvent()
    {
        var basket = NewEmptyBasket();
        var productId = Guid.CreateVersion7();
        basket.AddItem(productId, BasketTestData.Snapshot(), 1, UtcNow);
        _ = basket.PopDomainEvents();
        var versionBefore = basket.Version;

        var result = basket.ChangeQuantity(productId, newQuantity: 7, UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            basket.Items.Single().Quantity.Should().Be(7);
            basket.Version.Should().Be(versionBefore + 1);
            var evt = basket.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<ItemQuantityChangedDomainEvent>()
                .Subject;
            evt.OldQuantity.Should().Be(1);
            evt.NewQuantity.Should().Be(7);
        }
    }

    [Fact]
    public void ChangeQuantity_WhenSameValue_IsNoopNoEventNoVersionBump()
    {
        var basket = NewEmptyBasket();
        var productId = Guid.CreateVersion7();
        basket.AddItem(productId, BasketTestData.Snapshot(), 4, UtcNow);
        _ = basket.PopDomainEvents();
        var versionBefore = basket.Version;
        var lastModifiedBefore = basket.LastModifiedAtUtc;

        _fakeTimeProvider.Advance(TimeSpan.FromMinutes(10));
        var result = basket.ChangeQuantity(productId, newQuantity: 4, UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            basket.Version.Should().Be(versionBefore);
            basket.LastModifiedAtUtc.Should().Be(lastModifiedBefore);
            basket.PopDomainEvents().Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ChangeQuantity_WhenInvalidQuantity_FailsInvalidQuantity(int newQuantity)
    {
        var basket = NewEmptyBasket();
        var productId = Guid.CreateVersion7();
        basket.AddItem(productId, BasketTestData.Snapshot(), 1, UtcNow);
        _ = basket.PopDomainEvents();

        var result = basket.ChangeQuantity(productId, newQuantity, UtcNow);

        result.Should().BeFailure();
        ErrorCodeOf(result).Should().Be("Basket.InvalidQuantity");
    }

    [Fact]
    public void ChangeQuantity_WhenProductNotInBasket_FailsItemNotFound()
    {
        var basket = NewEmptyBasket();
        _ = basket.PopDomainEvents();

        var result = basket.ChangeQuantity(Guid.CreateVersion7(), newQuantity: 1, UtcNow);

        result.Should().BeFailure();
        ErrorCodeOf(result).Should().Be("Basket.ItemNotFound");
    }

    // ------------------------------------------------------------------
    // RefreshPrices
    // ------------------------------------------------------------------

    [Fact]
    public void RefreshPrices_WhenPriceChanged_ReplacesSnapshotAndEmitsChanges()
    {
        var basket = NewEmptyBasket();
        var p1 = Guid.CreateVersion7();
        var p2 = Guid.CreateVersion7();
        basket.AddItem(p1, BasketTestData.Snapshot(amount: 10m, sku: "SKU-1"), 2, UtcNow);
        basket.AddItem(p2, BasketTestData.Snapshot(amount: 20m, sku: "SKU-2"), 1, UtcNow);
        _ = basket.PopDomainEvents();
        var versionBefore = basket.Version;

        var fresh = new List<(Guid, ProductSnapshot)>
        {
            (p1, BasketTestData.Snapshot(amount: 12m, sku: "SKU-1")),
            (p2, BasketTestData.Snapshot(amount: 20m, sku: "SKU-2")),
        };

        var result = basket.RefreshPrices(fresh, UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            basket.Items.Single(i => i.ProductId == p1).Snapshot.Price.Amount.Should().Be(12m);
            basket.Items.Single(i => i.ProductId == p2).Snapshot.Price.Amount.Should().Be(20m);
            basket.Version.Should().Be(versionBefore + 1);
            var evt = basket.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<BasketPricesRefreshedDomainEvent>()
                .Subject;
            evt.Changes.Should().ContainSingle().Which.ProductId.Should().Be(p1);
        }
    }

    [Fact]
    public void RefreshPrices_WhenNoChange_IsNoop()
    {
        var basket = NewEmptyBasket();
        var p1 = Guid.CreateVersion7();
        basket.AddItem(p1, BasketTestData.Snapshot(amount: 10m), 1, UtcNow);
        _ = basket.PopDomainEvents();
        var versionBefore = basket.Version;

        var result = basket.RefreshPrices(
            [(p1, BasketTestData.Snapshot(amount: 10m))],
            UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            basket.Version.Should().Be(versionBefore);
            basket.PopDomainEvents().Should().BeEmpty();
        }
    }

    [Fact]
    public void RefreshPrices_WhenAllPricesEqualButMetadataChanged_DoesNotMutateInMemoryItems()
    {
        // sum1.HIGH-1 regression guard. Previously the aggregate swapped Sku/Name/
        // CapturedAt in place on the equal-price branch but did NOT call Touch() —
        // the handler then short-circuited on events.Count == 0 and skipped SaveAsync.
        // Net effect: in-memory state diverged from Redis (silent metadata loss on
        // next load). The fix preserves the frozen snapshot strictly when prices are
        // unchanged.
        var basket = NewEmptyBasket();
        var p1 = Guid.CreateVersion7();
        var original = BasketTestData.Snapshot(amount: 10m, sku: "SKU-OLD", name: "Old Name");
        basket.AddItem(p1, original, 1, UtcNow);
        _ = basket.PopDomainEvents();
        var versionBefore = basket.Version;

        // Same price, different Sku/Name/CapturedAt.
        var freshSameMetaSwap = BasketTestData.Snapshot(
            amount: 10m,
            sku: "SKU-NEW",
            name: "New Name",
            capturedAtUtc: UtcNow.AddDays(1));

        var result = basket.RefreshPrices([(p1, freshSameMetaSwap)], UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            basket.Version.Should().Be(versionBefore);
            basket.PopDomainEvents().Should().BeEmpty();
            var line = basket.Items.Single();
            line.Snapshot.Sku.Should().Be("SKU-OLD",
                "equal-price branch must preserve the frozen snapshot (invariant 6) — no silent metadata swap.");
            line.Snapshot.Name.Should().Be("Old Name");
            line.Snapshot.CapturedAtUtc.Should().Be(original.CapturedAtUtc);
        }
    }

    [Fact]
    public void RefreshPrices_WithUnknownProductIds_DoesNotAddThem()
    {
        var basket = NewEmptyBasket();
        var p1 = Guid.CreateVersion7();
        basket.AddItem(p1, BasketTestData.Snapshot(amount: 10m), 1, UtcNow);
        _ = basket.PopDomainEvents();

        var unknown = Guid.CreateVersion7();
        var result = basket.RefreshPrices(
            [(unknown, BasketTestData.Snapshot(amount: 99m, sku: "SKU-UNK"))],
            UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            basket.Items.Should().ContainSingle();
            basket.Items.Single().ProductId.Should().Be(p1);
        }
    }

    [Fact]
    public void RefreshPrices_WhenCurrencyDiffers_ThrowsDataIntegrityException()
    {
        var basket = NewEmptyBasket();
        var p1 = Guid.CreateVersion7();
        basket.AddItem(p1, BasketTestData.Snapshot(amount: 10m, currency: CurrencyCode.Usd), 1, UtcNow);

        var act = () => basket.RefreshPrices(
            [(p1, BasketTestData.Snapshot(amount: 10m, currency: CurrencyCode.Eur))],
            UtcNow);

        act.Should().Throw<DataIntegrityException>()
            .WithMessage("*currency*");
    }

    // ------------------------------------------------------------------
    // Clear
    // ------------------------------------------------------------------

    [Fact]
    public void Clear_WhenHasItems_EmptiesAndRaisesClearedEvent()
    {
        var basket = NewEmptyBasket();
        basket.AddItem(Guid.CreateVersion7(), BasketTestData.Snapshot(), 1, UtcNow);
        basket.AddItem(Guid.CreateVersion7(), BasketTestData.Snapshot(sku: "SKU-2"), 2, UtcNow);
        _ = basket.PopDomainEvents();
        var versionBefore = basket.Version;

        basket.Clear(UtcNow);

        using (new AssertionScope())
        {
            basket.Items.Should().BeEmpty();
            basket.Version.Should().Be(versionBefore + 1);
            basket.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<BasketClearedDomainEvent>();
        }
    }

    [Fact]
    public void Clear_WhenAlreadyEmpty_IsNoop()
    {
        var basket = NewEmptyBasket();
        _ = basket.PopDomainEvents();
        var versionBefore = basket.Version;
        var lastModifiedBefore = basket.LastModifiedAtUtc;

        _fakeTimeProvider.Advance(TimeSpan.FromMinutes(10));
        basket.Clear(UtcNow);

        using (new AssertionScope())
        {
            basket.Version.Should().Be(versionBefore);
            basket.LastModifiedAtUtc.Should().Be(lastModifiedBefore);
            basket.PopDomainEvents().Should().BeEmpty();
        }
    }

    // ------------------------------------------------------------------
    // Checkout
    // ------------------------------------------------------------------

    [Fact]
    public void Checkout_WhenEmpty_FailsEmptyBasket()
    {
        var basket = NewEmptyBasket();
        _ = basket.PopDomainEvents();
        var versionBefore = basket.Version;

        var result = basket.Checkout(
            Guid.CreateVersion7(),
            BasketTestData.Address(),
            BasketTestData.Address(),
            Guid.CreateVersion7(),
            UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            ErrorCodeOf(result).Should().Be("Basket.Empty");
            basket.Version.Should().Be(versionBefore);
            basket.PopDomainEvents().Should().BeEmpty();
        }
    }

    [Fact]
    public void Checkout_WhenHasItems_RaisesCheckedOutEventWithFullSnapshot()
    {
        var basket = NewEmptyBasket();
        var p1 = Guid.CreateVersion7();
        basket.AddItem(p1, BasketTestData.Snapshot(amount: 15m), 2, UtcNow);
        _ = basket.PopDomainEvents();
        var versionBefore = basket.Version;
        var correlationId = Guid.CreateVersion7();
        var shipping = BasketTestData.Address("US");
        var billing = BasketTestData.Address("CZ");
        var paymentMethodId = Guid.CreateVersion7();

        var result = basket.Checkout(correlationId, shipping, billing, paymentMethodId, UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            basket.Version.Should().Be(versionBefore + 1);
            var evt = basket.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<BasketCheckedOutDomainEvent>()
                .Subject;
            evt.UserId.Should().Be(basket.UserId);
            evt.CorrelationId.Should().Be(correlationId);
            evt.Snapshot.Items.Should().ContainSingle();
            evt.Snapshot.Total.Amount.Amount.Should().Be(30m);
            evt.ShippingAddress.Should().Be(shipping);
            evt.BillingAddress.Should().Be(billing);
            evt.PaymentMethodId.Should().Be(paymentMethodId);
        }
    }

    [Fact]
    public void Checkout_WhenCorrelationIdEmpty_ThrowsDataIntegrityException()
    {
        var basket = NewEmptyBasket();
        basket.AddItem(Guid.CreateVersion7(), BasketTestData.Snapshot(), 1, UtcNow);

        var act = () => basket.Checkout(
            Guid.Empty,
            BasketTestData.Address(),
            BasketTestData.Address(),
            Guid.CreateVersion7(),
            UtcNow);

        act.Should().Throw<DataIntegrityException>()
            .WithMessage("*CorrelationId*");
    }

    [Fact]
    public void Checkout_WhenPaymentMethodIdEmpty_ThrowsDataIntegrityException()
    {
        var basket = NewEmptyBasket();
        basket.AddItem(Guid.CreateVersion7(), BasketTestData.Snapshot(), 1, UtcNow);

        var act = () => basket.Checkout(
            Guid.CreateVersion7(),
            BasketTestData.Address(),
            BasketTestData.Address(),
            Guid.Empty,
            UtcNow);

        act.Should().Throw<DataIntegrityException>()
            .WithMessage("*PaymentMethodId*");
    }

    // ------------------------------------------------------------------
    // Total + LastModifiedAtUtc + Version monotonicity
    // ------------------------------------------------------------------

    [Fact]
    public void Total_ComputesSumOfLineValuesInBasketCurrency()
    {
        var basket = NewEmptyBasket();
        basket.AddItem(Guid.CreateVersion7(), BasketTestData.Snapshot(amount: 10m), 2, UtcNow);
        basket.AddItem(Guid.CreateVersion7(), BasketTestData.Snapshot(amount: 3.50m, sku: "SKU-2"), 4, UtcNow);

        basket.Total.Should().NotBeNull();
        basket.Total!.Amount.Amount.Should().Be(34m);
        basket.Total.Amount.Currency.Should().Be(CurrencyCode.Usd);
    }

    [Fact]
    public void LastModifiedAtUtc_AdvancesOnEverySuccessfulMutation()
    {
        var basket = NewEmptyBasket();
        _fakeTimeProvider.Advance(TimeSpan.FromMinutes(1));
        var t1 = UtcNow;
        basket.AddItem(Guid.CreateVersion7(), BasketTestData.Snapshot(), 1, t1);
        _fakeTimeProvider.Advance(TimeSpan.FromMinutes(5));
        var t2 = UtcNow;

        basket.Clear(t2);

        basket.LastModifiedAtUtc.Should().Be(t2);
    }

    [Fact]
    public void Version_IsMonotonicAcrossAllSuccessfulMutations()
    {
        var basket = NewEmptyBasket();
        var p1 = Guid.CreateVersion7();

        basket.AddItem(p1, BasketTestData.Snapshot(), 1, UtcNow);      // v1
        basket.ChangeQuantity(p1, 2, UtcNow);                            // v2
        basket.AddItem(Guid.CreateVersion7(), BasketTestData.Snapshot(sku: "SKU-2"), 1, UtcNow); // v3
        basket.RemoveItem(p1, UtcNow);                                   // v4
        basket.Clear(UtcNow);                                            // v5

        basket.Version.Should().Be(5);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private BasketAggregate NewEmptyBasket()
        => BasketAggregate.Create(Guid.CreateVersion7(), UtcNow);

    private static string ErrorCodeOf(FluentResults.ResultBase result)
        => ((ValidationError)result.Errors[0]).ErrorCode;
}
