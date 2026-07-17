using FluentResults.Extensions.FluentAssertions;
using Ordering.Domain.Orders.ValueObjects;
using Platform.SharedKernel.Errors;

namespace Ordering.UnitTests.Orders.ValueObjects;

public class ShipmentInfoTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 4, 22, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_Valid_TrimsAndReturnsInfo()
    {
        var result = ShipmentInfo.Create("  DHL  ", "  TRK-42  ", UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Carrier.Should().Be("DHL");
            result.Value.TrackingNumber.Should().Be("TRK-42");
            result.Value.ShippedAtUtc.Should().Be(UtcNow);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyCarrier_ReturnsCarrierEmptyError(string? carrier)
    {
        var result = ShipmentInfo.Create(carrier, "TRK-42", UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "ShipmentInfo.CarrierEmpty");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyTrackingNumber_ReturnsTrackingNumberEmptyError(string? tracking)
    {
        var result = ShipmentInfo.Create("DHL", tracking, UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "ShipmentInfo.TrackingNumberEmpty");
        }
    }

    [Fact]
    [Trait("Category", "boundary")]
    public void Create_CarrierTooLong_ReturnsCarrierTooLongError()
    {
        var result = ShipmentInfo.Create(
            new string('x', ShipmentInfo.MaxCarrierLength + 1),
            "TRK-42",
            UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "ShipmentInfo.CarrierTooLong");
        }
    }

    [Fact]
    [Trait("Category", "boundary")]
    public void Create_TrackingNumberTooLong_ReturnsTrackingNumberTooLongError()
    {
        var result = ShipmentInfo.Create(
            "DHL",
            new string('x', ShipmentInfo.MaxTrackingNumberLength + 1),
            UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "ShipmentInfo.TrackingNumberTooLong");
        }
    }
}
