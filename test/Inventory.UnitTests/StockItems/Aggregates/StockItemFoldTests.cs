using Inventory.Domain.StockItems;
using Inventory.Domain.StockItems.Events;
using Inventory.Domain.StockItems.ValueObjects;
using Platform.SharedKernel.Base.DomainEvents;

namespace Inventory.UnitTests.StockItems.Aggregates;

/// <summary>
/// Tests exercising <see cref="StockItem.Fold"/> directly — the pure reducer must be
/// equivalent to driving command methods in sequence.
/// </summary>
public class StockItemFoldTests
{
    private static readonly DateTimeOffset T0 = new(2026, 4, 24, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(15);

    [Fact]
    public void Fold_EmptyStream_ReturnsVersionZeroAggregate()
    {
        // Act
        var item = StockItem.Fold([]);

        // Assert
        using (new AssertionScope())
        {
            item.Version.Should().Be(0);
            item.Id.Should().Be(Guid.Empty);
            item.OnHand.Should().Be(0);
            item.Reserved.Should().Be(0);
            item.Available.Should().Be(0);
            item.Reservations.Should().BeEmpty();
            item.PopDomainEvents().Should().BeEmpty();
        }
    }

    [Fact]
    public void Fold_AfterInitEvent_HasVersionOneAndProductId()
    {
        // Arrange
        var productId = Guid.CreateVersion7();
        var events = new DomainEvent[]
        {
            new StockItemInitializedDomainEvent { ProductId = productId, OccurredOnUtc = T0 },
        };

        // Act
        var item = StockItem.Fold(events);

        // Assert
        using (new AssertionScope())
        {
            item.Version.Should().Be(1);
            item.Id.Should().Be(productId);
            item.OnHand.Should().Be(0);
            item.Reserved.Should().Be(0);
        }
    }

    [Fact]
    public void Fold_FullLifecycle_ProducesExpectedState()
    {
        // Arrange — init → receive(10) → reserve(3) → confirm → adjust(+5)
        // Expected final state: OnHand = 10 - 3 + 5 = 12, Reserved = 0, Version = 5.
        var productId = Guid.CreateVersion7();
        var reservationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var expires = T0 + DefaultTtl;

        var events = new DomainEvent[]
        {
            new StockItemInitializedDomainEvent { ProductId = productId, OccurredOnUtc = T0 },
            new StockReceivedDomainEvent
            {
                ProductId = productId,
                Quantity = 10,
                Source = "receiving-dock",
                ReceivedByUserId = null,
                OccurredOnUtc = T0.AddMinutes(1),
            },
            new StockReservedDomainEvent
            {
                ProductId = productId,
                ReservationId = reservationId,
                Quantity = 3,
                OrderId = orderId,
                ExpiresAtUtc = expires,
                OccurredOnUtc = T0.AddMinutes(2),
            },
            new ReservationConfirmedDomainEvent
            {
                ProductId = productId,
                ReservationId = reservationId,
                ConfirmedAtUtc = T0.AddMinutes(3),
                OccurredOnUtc = T0.AddMinutes(3),
            },
            new StockAdjustedDomainEvent
            {
                ProductId = productId,
                Delta = +5,
                Reason = "recount",
                AdjustedByUserId = null,
                OccurredOnUtc = T0.AddMinutes(4),
            },
        };

        // Act
        var item = StockItem.Fold(events);

        // Assert
        using (new AssertionScope())
        {
            item.Version.Should().Be(5);
            item.Id.Should().Be(productId);
            item.OnHand.Should().Be(12);
            item.Reserved.Should().Be(0);
            item.Available.Should().Be(12);
            var rid = ReservationId.Create(reservationId).Value;
            item.Reservations.Should().ContainKey(rid);
            item.Reservations[rid].Status.Should().Be(ReservationStatus.Confirmed);
        }
    }

    [Fact]
    public void Fold_IsPure_TwoInvocationsProduceEqualState()
    {
        // Arrange
        var events = BuildSampleStream().ToList();

        // Act
        var a = StockItem.Fold(events);
        var b = StockItem.Fold(events);

        // Assert
        using (new AssertionScope())
        {
            a.Version.Should().Be(b.Version);
            a.OnHand.Should().Be(b.OnHand);
            a.Reserved.Should().Be(b.Reserved);
            a.Available.Should().Be(b.Available);
            a.Id.Should().Be(b.Id);
            a.Reservations.Should().HaveSameCount(b.Reservations);
            foreach (var kvp in a.Reservations)
            {
                b.Reservations[kvp.Key].Should().Be(kvp.Value);
            }
        }
    }

    [Fact]
    public void Fold_RoundTrip_CommandsEmitEventsThatFoldIntoEquivalentState()
    {
        // Arrange — drive the aggregate via commands, capturing emitted events.
        var productId = Guid.CreateVersion7();
        var rid = ReservationId.Create(Guid.CreateVersion7()).Value;
        var orderId = Guid.CreateVersion7();

        var driven = StockItem.Fold([]);
        _ = driven.Initialize(productId, T0);
        _ = driven.ReceiveStock(20, StockSource.ReceivingDock, null, T0.AddMinutes(1));
        _ = driven.Reserve(rid, 5, orderId, DefaultTtl, T0.AddMinutes(2));
        _ = driven.ReleaseReservation(rid, ReleaseReason.Cancellation, T0.AddMinutes(3));
        _ = driven.AdjustStock(-2, "damage", null, T0.AddMinutes(4));

        var emittedEvents = driven.PopDomainEvents();

        // Act — rehydrate a fresh aggregate from the captured event stream.
        var rehydrated = StockItem.Fold(emittedEvents);

        // Assert
        using (new AssertionScope())
        {
            rehydrated.Version.Should().Be(driven.Version);
            rehydrated.Id.Should().Be(driven.Id);
            rehydrated.OnHand.Should().Be(driven.OnHand);
            rehydrated.Reserved.Should().Be(driven.Reserved);
            rehydrated.Available.Should().Be(driven.Available);
            rehydrated.Reservations.Should().HaveSameCount(driven.Reservations);
            rehydrated.Reservations[rid].Status.Should().Be(ReservationStatus.Released);
            rehydrated.PopDomainEvents().Should().BeEmpty(); // Fold does not re-raise events.
        }
    }

    private static IEnumerable<DomainEvent> BuildSampleStream()
    {
        var productId = Guid.CreateVersion7();
        var rid1 = Guid.CreateVersion7();
        var rid2 = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();

        yield return new StockItemInitializedDomainEvent { ProductId = productId, OccurredOnUtc = T0 };
        yield return new StockReceivedDomainEvent
        {
            ProductId = productId,
            Quantity = 15,
            Source = "receiving-dock",
            ReceivedByUserId = null,
            OccurredOnUtc = T0.AddSeconds(10),
        };
        yield return new StockReservedDomainEvent
        {
            ProductId = productId,
            ReservationId = rid1,
            Quantity = 4,
            OrderId = orderId,
            ExpiresAtUtc = T0.AddMinutes(15),
            OccurredOnUtc = T0.AddSeconds(20),
        };
        yield return new StockReservedDomainEvent
        {
            ProductId = productId,
            ReservationId = rid2,
            Quantity = 3,
            OrderId = Guid.CreateVersion7(),
            ExpiresAtUtc = T0.AddMinutes(15),
            OccurredOnUtc = T0.AddSeconds(30),
        };
        yield return new ReservationConfirmedDomainEvent
        {
            ProductId = productId,
            ReservationId = rid1,
            ConfirmedAtUtc = T0.AddSeconds(40),
            OccurredOnUtc = T0.AddSeconds(40),
        };
        yield return new ReservationReleasedDomainEvent
        {
            ProductId = productId,
            ReservationId = rid2,
            ReleaseReason = ReleaseReason.Expiry,
            ReleasedAtUtc = T0.AddSeconds(50),
            OccurredOnUtc = T0.AddSeconds(50),
        };
    }
}
