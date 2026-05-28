using Inventory.Domain.StockItems.Errors;
using Inventory.Domain.StockItems.ValueObjects;
using Platform.SharedKernel.Errors;

namespace Inventory.UnitTests.StockItems.Errors;

public class InventoryErrorsTests
{
    [Fact]
    public void InsufficientStock_ReturnsConflictErrorWithEntityNameAndCode()
    {
        // Arrange
        var productId = Guid.CreateVersion7();

        // Act
        var error = InventoryErrors.InsufficientStock(productId, requested: 8, available: 7);

        // Assert
        using (new AssertionScope())
        {
            error.Should().BeAssignableTo<ConflictError>();
            error.EntityName.Should().Be("StockItem");
            error.ErrorCode.Should().Be("Inventory.InsufficientStock");
            error.ProductId.Should().Be(productId);
            error.Requested.Should().Be(8);
            error.Available.Should().Be(7);
            error.Message.Should().Contain(productId.ToString()).And.Contain("8").And.Contain("7");
            error.Reasons.Should().BeEmpty();
        }
    }

    [Fact]
    public void Concurrency_ReturnsConflictErrorWithEntityNameAndCode()
    {
        // Arrange
        var streamId = Guid.CreateVersion7();

        // Act
        var error = InventoryErrors.Concurrency(streamId, expectedVersion: 7);

        // Assert
        using (new AssertionScope())
        {
            error.Should().BeAssignableTo<ConflictError>();
            error.EntityName.Should().Be("StockItem");
            error.ErrorCode.Should().Be("Inventory.Concurrency");
            error.StreamId.Should().Be(streamId);
            error.ExpectedVersion.Should().Be(7);
            error.Message.Should().Contain(streamId.ToString()).And.Contain("7");
            error.Reasons.Should().BeEmpty();
        }
    }

    [Fact]
    public void ReservationNotActive_ReturnsConflictErrorWithEntityNameAndCode()
    {
        // Arrange
        var productId = Guid.CreateVersion7();
        var reservationId = Guid.CreateVersion7();

        // Act
        var error = InventoryErrors.ReservationNotActive(productId, reservationId, ReservationStatus.Released);

        // Assert
        using (new AssertionScope())
        {
            error.Should().BeAssignableTo<ConflictError>();
            error.EntityName.Should().Be("Reservation");
            error.ErrorCode.Should().Be("Inventory.ReservationNotActive");
            error.ProductId.Should().Be(productId);
            error.ReservationId.Should().Be(reservationId);
            error.CurrentStatus.Should().Be(ReservationStatus.Released);
            error.Message.Should().Contain(reservationId.ToString()).And.Contain(productId.ToString());
            error.Reasons.Should().BeEmpty();
        }
    }

    [Fact]
    public void StockItemNotFound_CarriesEntityIdAndCode()
    {
        var productId = Guid.CreateVersion7();

        var error = InventoryErrors.StockItemNotFound(productId);

        using (new AssertionScope())
        {
            error.EntityName.Should().Be("StockItem");
            error.Id.Should().Be(productId);
            error.ErrorCode.Should().Be("Inventory.StockItem.NotFound");
            error.Message.Should().Contain("StockItem").And.Contain(productId.ToString());
        }
    }

    [Fact]
    public void ReservationNotFound_CarriesEntityIdAndCode()
    {
        var reservationId = Guid.CreateVersion7();

        var error = InventoryErrors.ReservationNotFound(reservationId);

        using (new AssertionScope())
        {
            error.EntityName.Should().Be("Reservation");
            error.Id.Should().Be(reservationId);
            error.ErrorCode.Should().Be("Inventory.Reservation.NotFound");
            error.Message.Should().Contain("Reservation").And.Contain(reservationId.ToString());
        }
    }
}
