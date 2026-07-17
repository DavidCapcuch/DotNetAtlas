using FluentResults.Extensions.FluentAssertions;
using Ordering.Domain.Orders;
using Ordering.Domain.Orders.ValueObjects;
using Platform.SharedKernel.Errors;

namespace Ordering.UnitTests.Orders.ValueObjects;

public class CancellationInfoTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 4, 22, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_Valid_ReturnsInfo()
    {
        var result = CancellationInfo.Create("  buyer abandoned  ", OrderStatus.Created, UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Reason.Should().Be("buyer abandoned");
            result.Value.AtStatus.Should().Be(OrderStatus.Created);
            result.Value.CancelledAtUtc.Should().Be(UtcNow);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyReason_ReturnsReasonEmptyError(string? reason)
    {
        var result = CancellationInfo.Create(reason, OrderStatus.Created, UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "CancellationInfo.ReasonEmpty");
        }
    }

    [Fact]
    [Trait("Category", "boundary")]
    public void Create_ReasonTooLong_ReturnsReasonTooLongError()
    {
        var tooLong = new string('x', CancellationInfo.MaxReasonLength + 1);

        var result = CancellationInfo.Create(tooLong, OrderStatus.Created, UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "CancellationInfo.ReasonTooLong");
        }
    }
}
