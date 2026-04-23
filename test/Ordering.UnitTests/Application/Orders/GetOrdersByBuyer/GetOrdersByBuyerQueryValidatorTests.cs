using FluentValidation.TestHelper;
using Ordering.Application.Orders.GetOrdersByBuyer;

namespace Ordering.UnitTests.Application.Orders.GetOrdersByBuyer;

/// <summary>
/// Validator-level tests for the paged buyer-orders query. The handler's
/// success path (projection of <c>Order</c> to the response DTO) is
/// exercised by integration tests against the real <c>OrderingDbContext</c>
/// in M4 — the test-project InMemory context intentionally ignores VO /
/// SmartEnum properties that the projection reads.
/// </summary>
public class GetOrdersByBuyerQueryValidatorTests
{
    private readonly GetOrdersByBuyerQueryValidator _validator = new();

    [Fact]
    public void Validate_Happy()
    {
        var q = new GetOrdersByBuyerQuery { BuyerId = Guid.CreateVersion7(), Skip = 0, Take = 20 };
        _validator.TestValidate(q).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyBuyerId_Fails()
    {
        var q = new GetOrdersByBuyerQuery { BuyerId = Guid.Empty, Skip = 0, Take = 20 };
        _validator.TestValidate(q).ShouldHaveValidationErrorFor(x => x.BuyerId);
    }

    [Fact]
    public void Validate_NegativeSkip_Fails()
    {
        var q = new GetOrdersByBuyerQuery { BuyerId = Guid.CreateVersion7(), Skip = -1, Take = 20 };
        _validator.TestValidate(q).ShouldHaveValidationErrorFor(x => x.Skip);
    }

    [Fact]
    public void Validate_TakeAboveMax_Fails()
    {
        var q = new GetOrdersByBuyerQuery { BuyerId = Guid.CreateVersion7(), Skip = 0, Take = 101 };
        _validator.TestValidate(q).ShouldHaveValidationErrorFor(x => x.Take);
    }

    [Theory]
    [InlineData("Created")]
    [InlineData("StockReserved")]
    [InlineData("PaymentCompleted")]
    [InlineData("Confirmed")]
    [InlineData("Shipped")]
    [InlineData("Delivered")]
    [InlineData("Cancelled")]
    [InlineData("Failed")]
    public void Validate_ValidStatus_Passes(string status)
    {
        var q = new GetOrdersByBuyerQuery { BuyerId = Guid.CreateVersion7(), Status = status, Skip = 0, Take = 20 };
        _validator.TestValidate(q).ShouldNotHaveValidationErrorFor(x => x.Status);
    }

    [Fact]
    public void Validate_NullStatus_Passes()
    {
        var q = new GetOrdersByBuyerQuery { BuyerId = Guid.CreateVersion7(), Status = null, Skip = 0, Take = 20 };
        _validator.TestValidate(q).ShouldNotHaveValidationErrorFor(x => x.Status);
    }

    [Theory]
    [InlineData("Bogus")]
    [InlineData("confirmed")] // case-sensitive — SmartEnum names are PascalCase
    public void Validate_InvalidStatus_Fails(string status)
    {
        var q = new GetOrdersByBuyerQuery { BuyerId = Guid.CreateVersion7(), Status = status, Skip = 0, Take = 20 };
        _validator.TestValidate(q).ShouldHaveValidationErrorFor(x => x.Status);
    }
}
