using FluentResults.Extensions.FluentAssertions;
using Inventory.Domain.StockItems.ValueObjects;
using Platform.SharedKernel.Errors;

namespace Inventory.UnitTests.StockItems.ValueObjects;

public class ReservationIdTests
{
    [Fact]
    public void Create_WhenNonEmpty_ReturnsSuccess()
    {
        // Arrange
        var guid = Guid.CreateVersion7();

        // Act
        var result = ReservationId.Create(guid);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Value.Should().Be(guid);
        }
    }

    [Fact]
    public void Create_WhenEmptyGuid_ReturnsFailureWithReservationIdEmpty()
    {
        // Act
        var result = ReservationId.Create(Guid.Empty);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "ReservationId.Empty");
        }
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        // Arrange
        var guid = Guid.CreateVersion7();
        var a = ReservationId.Create(guid).Value;
        var b = ReservationId.Create(guid).Value;

        // Assert
        a.Should().Be(b);
    }
}
