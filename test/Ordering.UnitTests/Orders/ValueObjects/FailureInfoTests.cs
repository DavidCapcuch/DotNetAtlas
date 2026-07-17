using FluentResults.Extensions.FluentAssertions;
using Ordering.Domain.Orders;
using Ordering.Domain.Orders.ValueObjects;
using Platform.SharedKernel.Errors;

namespace Ordering.UnitTests.Orders.ValueObjects;

public class FailureInfoTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 4, 22, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_Valid_ReturnsInfo()
    {
        var result = FailureInfo.Create(
            "PAYMENT_FAILED", "Card declined.", OrderStatus.StockReserved, UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.ErrorCode.Should().Be("PAYMENT_FAILED");
            result.Value.ErrorMessage.Should().Be("Card declined.");
            result.Value.AtStatus.Should().Be(OrderStatus.StockReserved);
            result.Value.FailedAtUtc.Should().Be(UtcNow);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyErrorCode_ReturnsErrorCodeEmptyError(string? code)
    {
        var result = FailureInfo.Create(code, "msg", OrderStatus.Created, UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "FailureInfo.ErrorCodeEmpty");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyErrorMessage_ReturnsErrorMessageEmptyError(string? message)
    {
        var result = FailureInfo.Create("CODE", message, OrderStatus.Created, UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "FailureInfo.ErrorMessageEmpty");
        }
    }

    [Fact]
    [Trait("Category", "boundary")]
    public void Create_ErrorCodeTooLong_ReturnsErrorCodeTooLongError()
    {
        var tooLong = new string('x', FailureInfo.MaxErrorCodeLength + 1);

        var result = FailureInfo.Create(tooLong, "msg", OrderStatus.Created, UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "FailureInfo.ErrorCodeTooLong");
        }
    }

    [Fact]
    [Trait("Category", "boundary")]
    public void Create_ErrorMessageTooLong_ReturnsErrorMessageTooLongError()
    {
        var tooLong = new string('x', FailureInfo.MaxErrorMessageLength + 1);

        var result = FailureInfo.Create("CODE", tooLong, OrderStatus.Created, UtcNow);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "FailureInfo.ErrorMessageTooLong");
        }
    }
}
