using Inventory.Domain.StockItems.Errors;
using Inventory.Domain.StockItems.ValueObjects;

namespace Inventory.UnitTests.StockItems.Errors;

public class InventoryErrorsTests
{
    [Fact]
    public void InsufficientStock_CarriesMetadataPerErrorTaxonomy()
    {
        // Arrange
        var productId = Guid.CreateVersion7();

        // Act
        var error = InventoryErrors.InsufficientStock(productId, requested: 8, available: 7);

        // Assert
        using (new AssertionScope())
        {
            error.ProductId.Should().Be(productId);
            error.Requested.Should().Be(8);
            error.Available.Should().Be(7);
            error.Message.Should().Contain(productId.ToString()).And.Contain("8").And.Contain("7");
            error.Metadata["ErrorCode"].Should().Be("Inventory.InsufficientStock");
            error.Metadata["ProductId"].Should().Be(productId);
            error.Metadata["Requested"].Should().Be(8);
            error.Metadata["Available"].Should().Be(7);
            error.Reasons.Should().BeEmpty();
        }
    }

    [Fact]
    public void Concurrency_CarriesMetadataPerErrorTaxonomy()
    {
        // Arrange
        var streamId = Guid.CreateVersion7();

        // Act
        var error = InventoryErrors.Concurrency(streamId, expectedVersion: 7);

        // Assert
        using (new AssertionScope())
        {
            error.StreamId.Should().Be(streamId);
            error.ExpectedVersion.Should().Be(7);
            error.Message.Should().Contain(streamId.ToString()).And.Contain("7");
            error.Metadata["ErrorCode"].Should().Be("Inventory.Concurrency");
            error.Reasons.Should().BeEmpty();
        }
    }

    [Fact]
    public void ReservationNotActive_CarriesMetadataAndStatus()
    {
        // Arrange
        var productId = Guid.CreateVersion7();
        var reservationId = Guid.CreateVersion7();

        // Act
        var error = InventoryErrors.ReservationNotActive(productId, reservationId, ReservationStatus.Released);

        // Assert
        using (new AssertionScope())
        {
            error.ProductId.Should().Be(productId);
            error.ReservationId.Should().Be(reservationId);
            error.CurrentStatus.Should().Be(ReservationStatus.Released);
            error.Metadata["ErrorCode"].Should().Be("Inventory.ReservationNotActive");
            error.Metadata["ProductId"].Should().Be(productId);
            error.Metadata["ReservationId"].Should().Be(reservationId);
            error.Metadata["CurrentStatus"].Should().Be(ReservationStatus.Released);
            error.Reasons.Should().BeEmpty();
        }
    }
}
