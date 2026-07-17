using FluentValidation.TestHelper;
using Ordering.Application.Orders.GetOrderById;

namespace Ordering.UnitTests.Application.Orders.GetOrderById;

/// <summary>
/// Validator-level tests for the single-order read query. Pins the
/// IsAdmin → BuyerId=Guid.Empty contract symmetric with
/// <c>CancelOrderCommandValidator</c> — admin service-account tokens
/// whose JWT <c>sub</c> is not a parseable Guid must reach the handler
/// (where the IsAdmin authorisation branch runs), not be rejected
/// upstream as a 400 by the validation behaviour.
/// </summary>
public class GetOrderByIdQueryValidatorTests
{
    private readonly GetOrderByIdQueryValidator _validator = new();

    [Fact]
    public void Validate_BuyerHappyPath_Passes()
    {
        var q = new GetOrderByIdQuery
        {
            OrderId = Guid.CreateVersion7(),
            BuyerId = Guid.CreateVersion7(),
            IsAdmin = false,
        };
        _validator.TestValidate(q).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    [Trait("Category", "security")]
    public void Validate_AdminWithEmptyBuyerId_Passes()
    {
        var q = new GetOrderByIdQuery
        {
            OrderId = Guid.CreateVersion7(),
            BuyerId = Guid.Empty,
            IsAdmin = true,
        };
        _validator.TestValidate(q).ShouldNotHaveValidationErrorFor(x => x.BuyerId);
    }

    [Fact]
    [Trait("Category", "security")]
    public void Validate_BuyerWithEmptyBuyerId_Fails()
    {
        var q = new GetOrderByIdQuery
        {
            OrderId = Guid.CreateVersion7(),
            BuyerId = Guid.Empty,
            IsAdmin = false,
        };
        _validator.TestValidate(q).ShouldHaveValidationErrorFor(x => x.BuyerId);
    }

    [Fact]
    public void Validate_EmptyOrderId_Fails()
    {
        var q = new GetOrderByIdQuery
        {
            OrderId = Guid.Empty,
            BuyerId = Guid.CreateVersion7(),
            IsAdmin = false,
        };
        _validator.TestValidate(q).ShouldHaveValidationErrorFor(x => x.OrderId);
    }
}
