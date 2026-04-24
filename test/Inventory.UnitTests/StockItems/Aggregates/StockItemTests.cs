using FluentResults.Extensions.FluentAssertions;
using Inventory.Domain.StockItems;
using Inventory.Domain.StockItems.Errors;
using Inventory.Domain.StockItems.Events;
using Inventory.Domain.StockItems.ValueObjects;
using Platform.SharedKernel.Exceptions;

namespace Inventory.UnitTests.StockItems.Aggregates;

/// <summary>
/// Behavioural tests for <see cref="StockItem"/> driven by
/// <c>docs/bc-design/example-mapping/inventory.md</c> sessions plus domain-level
/// coverage of each reducer in <c>inventory.md § 5</c>.
/// </summary>
public class StockItemTests
{
    private static readonly DateTimeOffset T0 = new(2026, 4, 24, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(15);

    // ============================================================
    // Initialize
    // ============================================================

    [Fact]
    public void Initialize_OnFreshStockItem_SetsVersionToOneAndProductId()
    {
        // Arrange
        var item = StockItem.Fold([]);
        var productId = Guid.CreateVersion7();

        // Act
        var result = item.Initialize(productId, T0);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            item.Version.Should().Be(1);
            item.ProductId.Should().Be(productId);
            item.Id.Should().Be(productId);
            item.OnHand.Should().Be(0);
            item.Reserved.Should().Be(0);
            item.Available.Should().Be(0);
            item.Reservations.Should().BeEmpty();
            item.PopDomainEvents().Should().ContainSingle().Which.Should().BeOfType<StockItemInitializedEvent>();
        }
    }

    [Fact]
    public void Initialize_WhenAlreadyInitialized_ThrowsDataIntegrityException()
    {
        // Arrange
        var item = CreateInitialized();

        // Act
        var act = () => item.Initialize(Guid.CreateVersion7(), T0);

        // Assert
        act.Should().Throw<DataIntegrityException>()
           .Which.ErrorCode.Should().Be("Inventory.StreamAlreadyInitialized");
    }

    [Fact]
    public void Initialize_WithEmptyProductId_ThrowsDataIntegrityException()
    {
        // Arrange
        var item = StockItem.Fold([]);

        // Act
        var act = () => item.Initialize(Guid.Empty, T0);

        // Assert
        act.Should().Throw<DataIntegrityException>()
           .Which.ErrorCode.Should().Be("Inventory.ProductIdRequired");
    }

    // ============================================================
    // ReceiveStock
    // ============================================================

    [Fact]
    public void ReceiveStock_IncrementsOnHandByQuantityAndEmitsEvent()
    {
        // Arrange
        var item = CreateInitialized();
        _ = item.PopDomainEvents();

        // Act
        var result = item.ReceiveStock(quantity: 10, source: StockSource.ReceivingDock, receivedByUserId: null, occurredOnUtc: T0);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            item.OnHand.Should().Be(10);
            item.Available.Should().Be(10);
            item.Version.Should().Be(2);
            var evt = item.PopDomainEvents().Should().ContainSingle()
                .Which.Should().BeOfType<StockReceivedEvent>().Subject;
            evt.Quantity.Should().Be(10);
            evt.Source.Should().Be("receiving-dock");
        }
    }

    [Fact]
    public void ReceiveStock_WhenUninitialized_ThrowsDataIntegrityException()
    {
        // Arrange
        var item = StockItem.Fold([]);

        // Act
        var act = () => item.ReceiveStock(10, StockSource.ReceivingDock, null, T0);

        // Assert
        act.Should().Throw<DataIntegrityException>()
           .Which.ErrorCode.Should().Be("Inventory.StreamNotInitialized");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ReceiveStock_WhenQuantityNotPositive_ThrowsDataIntegrityException(int qty)
    {
        // Arrange
        var item = CreateInitialized();

        // Act
        var act = () => item.ReceiveStock(qty, StockSource.ReceivingDock, null, T0);

        // Assert
        act.Should().Throw<DataIntegrityException>()
           .Which.ErrorCode.Should().Be("Inventory.QuantityMustBePositive");
    }

    // ============================================================
    // Reserve — example-mapping Session 2
    // ============================================================

    [Fact]
    public void Reserve_WhenSufficientStock_EmitsEventAndIncrementsReserved()
    {
        // Arrange — inventory.md example-mapping Session 2 "sufficient stock available"
        var item = CreateInitializedWithStock(onHand: 10, reservedAlready: 3);
        _ = item.PopDomainEvents();
        var rid = NewRid();
        var orderId = Guid.CreateVersion7();

        // Act
        var result = item.Reserve(rid, quantity: 7, orderId, DefaultTtl, T0);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            item.OnHand.Should().Be(10);
            item.Reserved.Should().Be(10);
            item.Available.Should().Be(0);
            item.Reservations.Should().ContainKey(rid);
            item.Reservations[rid].Status.Should().Be(ReservationStatus.Active);
            item.Reservations[rid].OrderId.Should().Be(orderId);
            var evt = item.PopDomainEvents().Should().ContainSingle()
                .Which.Should().BeOfType<StockReservedEvent>().Subject;
            evt.ReservationId.Should().Be(rid.Value);
            evt.Quantity.Should().Be(7);
            evt.ExpiresAtUtc.Should().Be(T0 + DefaultTtl);
        }
    }

    [Fact]
    public void Reserve_WhenAvailableLessThanQuantity_ReturnsInsufficientStock_NoEvent()
    {
        // Arrange — Session 2 "request exceeds Available"
        var item = CreateInitializedWithStock(onHand: 10, reservedAlready: 3);
        _ = item.PopDomainEvents();
        var versionBefore = item.Version;
        var reservedBefore = item.Reserved;
        var rid = NewRid();

        // Act
        var result = item.Reserve(rid, quantity: 8, Guid.CreateVersion7(), DefaultTtl, T0);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle()
                  .Which.Should().BeOfType<InsufficientStockError>()
                  .Which.Available.Should().Be(7);
            item.Version.Should().Be(versionBefore);
            item.Reserved.Should().Be(reservedBefore);
            item.Reservations.Should().NotContainKey(rid);
            item.PopDomainEvents().Should().BeEmpty();
        }
    }

    [Fact]
    public void Reserve_SetsExpiresAtUtcAsOccurredPlusTtl()
    {
        // Arrange
        var item = CreateInitializedWithStock(onHand: 10, reservedAlready: 0);
        var ttl = TimeSpan.FromMinutes(5);
        var rid = NewRid();

        // Act
        var result = item.Reserve(rid, 1, Guid.CreateVersion7(), ttl, T0);

        // Assert
        result.Should().BeSuccess();
        item.Reservations[rid].ExpiresAtUtc.Should().Be(T0 + ttl);
    }

    [Fact]
    public void Reserve_WhenDuplicateReservationId_ThrowsDataIntegrityException()
    {
        // Arrange
        var item = CreateInitializedWithStock(onHand: 10, reservedAlready: 0);
        var rid = NewRid();
        _ = item.Reserve(rid, 1, Guid.CreateVersion7(), DefaultTtl, T0);
        _ = item.PopDomainEvents();

        // Act
        var act = () => item.Reserve(rid, 1, Guid.CreateVersion7(), DefaultTtl, T0);

        // Assert
        act.Should().Throw<DataIntegrityException>()
           .Which.ErrorCode.Should().Be("Inventory.ReservationAlreadyExists");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Reserve_WhenQuantityNotPositive_ThrowsDataIntegrityException(int qty)
    {
        // Arrange
        var item = CreateInitializedWithStock(onHand: 10, reservedAlready: 0);

        // Act
        var act = () => item.Reserve(NewRid(), qty, Guid.CreateVersion7(), DefaultTtl, T0);

        // Assert
        act.Should().Throw<DataIntegrityException>()
           .Which.ErrorCode.Should().Be("Inventory.QuantityMustBePositive");
    }

    [Fact]
    public void Reserve_WhenTtlNotPositive_ThrowsDataIntegrityException()
    {
        // Arrange
        var item = CreateInitializedWithStock(onHand: 10, reservedAlready: 0);

        // Act
        var act = () => item.Reserve(NewRid(), 1, Guid.CreateVersion7(), TimeSpan.Zero, T0);

        // Assert
        act.Should().Throw<DataIntegrityException>()
           .Which.ErrorCode.Should().Be("Inventory.TtlMustBePositive");
    }

    [Fact]
    public void Reserve_WhenOrderIdEmpty_ThrowsDataIntegrityException()
    {
        // Arrange
        var item = CreateInitializedWithStock(onHand: 10, reservedAlready: 0);

        // Act
        var act = () => item.Reserve(NewRid(), 1, Guid.Empty, DefaultTtl, T0);

        // Assert
        act.Should().Throw<DataIntegrityException>()
           .Which.ErrorCode.Should().Be("Inventory.OrderIdRequired");
    }

    // ============================================================
    // ConfirmReservation — example-mapping Session 3
    // ============================================================

    [Fact]
    public void ConfirmReservation_WhenActive_DecrementsOnHandAndReserved()
    {
        // Arrange — Session 3 "confirm commits the reservation"
        var item = CreateInitializedWithStock(onHand: 10, reservedAlready: 0);
        var rid = SeedReservation(item, quantity: 3);
        _ = item.PopDomainEvents();
        var confirmedAt = T0.AddMinutes(5);

        // Act
        var result = item.ConfirmReservation(rid, confirmedAt);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            item.OnHand.Should().Be(7);
            item.Reserved.Should().Be(0);
            item.Available.Should().Be(7);
            item.Reservations[rid].Status.Should().Be(ReservationStatus.Confirmed);
            var evt = item.PopDomainEvents().Should().ContainSingle()
                .Which.Should().BeOfType<ReservationConfirmedEvent>().Subject;
            evt.ReservationId.Should().Be(rid.Value);
            evt.ConfirmedAtUtc.Should().Be(confirmedAt);
        }
    }

    [Fact]
    public void ConfirmReservation_WhenAlreadyConfirmed_IsNoOpReturnsOk_NoEvent()
    {
        // Arrange — Session 3 "confirm is replayed"
        var item = CreateInitializedWithStock(onHand: 10, reservedAlready: 0);
        var rid = SeedReservation(item, 3);
        _ = item.ConfirmReservation(rid, T0);
        _ = item.PopDomainEvents();
        var versionBefore = item.Version;
        var onHandBefore = item.OnHand;

        // Act
        var result = item.ConfirmReservation(rid, T0.AddSeconds(1));

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            item.Version.Should().Be(versionBefore);
            item.OnHand.Should().Be(onHandBefore);
            item.PopDomainEvents().Should().BeEmpty();
        }
    }

    [Fact]
    public void ConfirmReservation_WhenReleased_ReturnsReservationNotActive_NoEvent()
    {
        // Arrange — Session 3 "confirm arrives on released reservation" + Session 1 R4.
        var item = CreateInitializedWithStock(onHand: 10, reservedAlready: 0);
        var rid = SeedReservation(item, 3);
        _ = item.ReleaseReservation(rid, ReleaseReason.Cancellation, T0);
        _ = item.PopDomainEvents();
        var versionBefore = item.Version;

        // Act
        var result = item.ConfirmReservation(rid, T0);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle()
                  .Which.Should().BeOfType<ReservationNotActiveError>()
                  .Which.CurrentStatus.Should().Be(ReservationStatus.Released);
            item.Version.Should().Be(versionBefore);
            item.PopDomainEvents().Should().BeEmpty();
        }
    }

    [Fact]
    public void ConfirmReservation_WhenUnknownRid_ThrowsDataIntegrityException()
    {
        // Arrange
        var item = CreateInitializedWithStock(onHand: 10, reservedAlready: 0);

        // Act
        var act = () => item.ConfirmReservation(NewRid(), T0);

        // Assert
        act.Should().Throw<DataIntegrityException>()
           .Which.ErrorCode.Should().Be("Inventory.ReservationUnknown");
    }

    [Fact]
    public void ConfirmReservation_WhenUninitialized_ThrowsDataIntegrityException()
    {
        // Arrange
        var item = StockItem.Fold([]);

        // Act
        var act = () => item.ConfirmReservation(NewRid(), T0);

        // Assert
        act.Should().Throw<DataIntegrityException>()
           .Which.ErrorCode.Should().Be("Inventory.StreamNotInitialized");
    }

    // ============================================================
    // ReleaseReservation — example-mapping Session 1
    // ============================================================

    [Theory]
    [InlineData(ReleaseReason.Compensation)]
    [InlineData(ReleaseReason.Expiry)]
    [InlineData(ReleaseReason.Cancellation)]
    public void ReleaseReservation_WhenActive_DecrementsReservedAndEmitsEvent(ReleaseReason reason)
    {
        // Arrange — Session 1 "buyer abandons and TTL fires" (covers Expiry path)
        var item = CreateInitializedWithStock(onHand: 10, reservedAlready: 0);
        var rid = SeedReservation(item, 3);
        _ = item.PopDomainEvents();

        // Act
        var result = item.ReleaseReservation(rid, reason, T0);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            item.OnHand.Should().Be(10);
            item.Reserved.Should().Be(0);
            item.Available.Should().Be(10);
            item.Reservations[rid].Status.Should().Be(ReservationStatus.Released);
            var evt = item.PopDomainEvents().Should().ContainSingle()
                .Which.Should().BeOfType<ReservationReleasedEvent>().Subject;
            evt.ReleaseReason.Should().Be(reason);
            evt.ReleasedAtUtc.Should().Be(T0);
        }
    }

    [Fact]
    public void ReleaseReservation_WhenAlreadyReleased_IsNoOpReturnsOk_NoEvent()
    {
        // Arrange — Session 1 R5 "duplicate release attempt"
        var item = CreateInitializedWithStock(onHand: 10, reservedAlready: 0);
        var rid = SeedReservation(item, 3);
        _ = item.ReleaseReservation(rid, ReleaseReason.Expiry, T0);
        _ = item.PopDomainEvents();
        var versionBefore = item.Version;

        // Act
        var result = item.ReleaseReservation(rid, ReleaseReason.Expiry, T0.AddSeconds(1));

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            item.Version.Should().Be(versionBefore);
            item.PopDomainEvents().Should().BeEmpty();
        }
    }

    [Fact]
    public void ReleaseReservation_WhenConfirmed_ReturnsReservationNotActive()
    {
        // Arrange
        var item = CreateInitializedWithStock(onHand: 10, reservedAlready: 0);
        var rid = SeedReservation(item, 3);
        _ = item.ConfirmReservation(rid, T0);
        _ = item.PopDomainEvents();

        // Act
        var result = item.ReleaseReservation(rid, ReleaseReason.Compensation, T0);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle()
                  .Which.Should().BeOfType<ReservationNotActiveError>()
                  .Which.CurrentStatus.Should().Be(ReservationStatus.Confirmed);
            item.PopDomainEvents().Should().BeEmpty();
        }
    }

    [Fact]
    public void ReleaseReservation_WhenUnknownRid_ThrowsDataIntegrityException()
    {
        // Arrange
        var item = CreateInitializedWithStock(onHand: 10, reservedAlready: 0);

        // Act
        var act = () => item.ReleaseReservation(NewRid(), ReleaseReason.Cancellation, T0);

        // Assert
        act.Should().Throw<DataIntegrityException>()
           .Which.ErrorCode.Should().Be("Inventory.ReservationUnknown");
    }

    // ============================================================
    // AdjustStock
    // ============================================================

    [Fact]
    public void AdjustStock_PositiveDelta_IncrementsOnHand()
    {
        // Arrange
        var item = CreateInitializedWithStock(onHand: 10, reservedAlready: 0);
        _ = item.PopDomainEvents();

        // Act
        var result = item.AdjustStock(delta: +5, reason: "recount", adjustedByUserId: null, occurredOnUtc: T0);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            item.OnHand.Should().Be(15);
            item.Available.Should().Be(15);
            var evt = item.PopDomainEvents().Should().ContainSingle()
                .Which.Should().BeOfType<StockAdjustedEvent>().Subject;
            evt.Delta.Should().Be(5);
            evt.Reason.Should().Be("recount");
        }
    }

    [Fact]
    public void AdjustStock_NegativeDeltaWithinBounds_DecrementsOnHand()
    {
        // Arrange
        var item = CreateInitializedWithStock(onHand: 10, reservedAlready: 3);
        _ = item.PopDomainEvents();

        // Act — writing off 2 damaged units; OnHand=8, Reserved=3, Available=5.
        var result = item.AdjustStock(-2, "damage", null, T0);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            item.OnHand.Should().Be(8);
            item.Reserved.Should().Be(3);
            item.Available.Should().Be(5);
        }
    }

    [Fact]
    public void AdjustStock_WhenZero_IsNoOpNoEvent()
    {
        // Arrange
        var item = CreateInitializedWithStock(onHand: 10, reservedAlready: 0);
        _ = item.PopDomainEvents();
        var versionBefore = item.Version;

        // Act
        var result = item.AdjustStock(0, "no change", null, T0);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            item.Version.Should().Be(versionBefore);
            item.PopDomainEvents().Should().BeEmpty();
        }
    }

    [Fact]
    public void AdjustStock_WhenResultNegative_ThrowsAdjustmentBelowZero()
    {
        // Arrange
        var item = CreateInitializedWithStock(onHand: 5, reservedAlready: 0);

        // Act
        var act = () => item.AdjustStock(-10, "recount", null, T0);

        // Assert
        act.Should().Throw<DataIntegrityException>()
           .Which.ErrorCode.Should().Be("Inventory.AdjustmentBelowZero");
    }

    [Fact]
    public void AdjustStock_WhenResultBelowReservations_ThrowsAdjustmentBelowReservations()
    {
        // Arrange — OnHand=10, Reserved=5. Subtract 6 → OnHand would be 4, Available -1.
        var item = CreateInitializedWithStock(onHand: 10, reservedAlready: 5);

        // Act
        var act = () => item.AdjustStock(-6, "damage", null, T0);

        // Assert
        act.Should().Throw<DataIntegrityException>()
           .Which.ErrorCode.Should().Be("Inventory.AdjustmentBelowReservations");
    }

    [Fact]
    public void AdjustStock_WhenReasonEmpty_ThrowsReasonRequired()
    {
        // Arrange
        var item = CreateInitializedWithStock(onHand: 10, reservedAlready: 0);

        // Act
        var act = () => item.AdjustStock(+1, "  ", null, T0);

        // Assert
        act.Should().Throw<DataIntegrityException>()
           .Which.ErrorCode.Should().Be("Inventory.ReasonRequired");
    }

    // ============================================================
    // Helpers
    // ============================================================

    private static ReservationId NewRid()
        => ReservationId.Create(Guid.CreateVersion7()).Value;

    private static StockItem CreateInitialized()
    {
        var item = StockItem.Fold([]);
        _ = item.Initialize(Guid.CreateVersion7(), T0);
        return item;
    }

    private static StockItem CreateInitializedWithStock(int onHand, int reservedAlready)
    {
        if (reservedAlready > onHand)
        {
            throw new ArgumentException("Cannot preseed reserved > onHand.", nameof(reservedAlready));
        }

        var item = CreateInitialized();
        if (onHand > 0)
        {
            _ = item.ReceiveStock(onHand, StockSource.ReceivingDock, null, T0);
        }

        if (reservedAlready > 0)
        {
            _ = item.Reserve(NewRid(), reservedAlready, Guid.CreateVersion7(), DefaultTtl, T0);
        }

        return item;
    }

    private static ReservationId SeedReservation(StockItem item, int quantity)
    {
        var rid = NewRid();
        var reserve = item.Reserve(rid, quantity, Guid.CreateVersion7(), DefaultTtl, T0);
        reserve.IsSuccess.Should().BeTrue();
        return rid;
    }
}
