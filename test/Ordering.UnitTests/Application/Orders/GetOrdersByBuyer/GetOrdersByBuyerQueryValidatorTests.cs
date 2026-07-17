using FluentValidation.TestHelper;
using Ordering.Application.Orders.GetOrdersByBuyer;

namespace Ordering.UnitTests.Application.Orders.GetOrdersByBuyer;

/// <summary>
/// Validator-level tests for the paged buyer-orders query. The handler's
/// success path (SQL-side projection to <see cref="OrderSummaryDto"/>,
/// including the <c>LastStatusChangeAtUtc</c> COALESCE chain) is exercised
/// by integration tests against the real <c>OrderingDbContext</c> — EF's
/// InMemory provider cannot translate the conditional projection on the
/// owned <c>Shipment</c> VO.
/// </summary>
public class GetOrdersByBuyerQueryValidatorTests
{
    private readonly GetOrdersByBuyerQueryValidator _validator = new();

    [Fact]
    public void Validate_Happy()
    {
        var q = new GetOrdersByBuyerQuery { BuyerId = Guid.CreateVersion7(), PageNumber = 1, PageSize = 20 };
        _validator.TestValidate(q).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyBuyerId_Fails()
    {
        var q = new GetOrdersByBuyerQuery { BuyerId = Guid.Empty, PageNumber = 1, PageSize = 20 };
        _validator.TestValidate(q).ShouldHaveValidationErrorFor(x => x.BuyerId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [Trait("Category", "boundary")]
    public void Validate_PageNumberBelowMin_Fails(int pageNumber)
    {
        var q = new GetOrdersByBuyerQuery { BuyerId = Guid.CreateVersion7(), PageNumber = pageNumber, PageSize = 20 };
        _validator.TestValidate(q).ShouldHaveValidationErrorFor(x => x.PageNumber);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [Trait("Category", "boundary")]
    public void Validate_PageSizeOutOfRange_Fails(int pageSize)
    {
        var q = new GetOrdersByBuyerQuery { BuyerId = Guid.CreateVersion7(), PageNumber = 1, PageSize = pageSize };
        _validator.TestValidate(q).ShouldHaveValidationErrorFor(x => x.PageSize);
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
        var q = new GetOrdersByBuyerQuery { BuyerId = Guid.CreateVersion7(), Status = status, PageNumber = 1, PageSize = 20 };
        _validator.TestValidate(q).ShouldNotHaveValidationErrorFor(x => x.Status);
    }

    [Fact]
    public void Validate_NullStatus_Passes()
    {
        var q = new GetOrdersByBuyerQuery { BuyerId = Guid.CreateVersion7(), Status = null, PageNumber = 1, PageSize = 20 };
        _validator.TestValidate(q).ShouldNotHaveValidationErrorFor(x => x.Status);
    }

    [Theory]
    [InlineData("Bogus")]
    [InlineData("confirmed")] // case-sensitive — SmartEnum names are PascalCase
    public void Validate_InvalidStatus_Fails(string status)
    {
        var q = new GetOrdersByBuyerQuery { BuyerId = Guid.CreateVersion7(), Status = status, PageNumber = 1, PageSize = 20 };
        _validator.TestValidate(q).ShouldHaveValidationErrorFor(x => x.Status);
    }
}
