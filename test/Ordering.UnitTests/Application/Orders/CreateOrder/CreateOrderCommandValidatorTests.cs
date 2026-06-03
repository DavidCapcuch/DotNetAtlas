using FluentValidation.TestHelper;
using Ordering.Application.Orders.CreateOrder;
using Ordering.UnitTests.Application.Common;

namespace Ordering.UnitTests.Application.Orders.CreateOrder;

public class CreateOrderCommandValidatorTests
{
    private readonly CreateOrderCommandValidator _validator = new();

    private static CreateOrderCommand Valid() => new()
    {
        OrderId = Guid.CreateVersion7(),
        CorrelationId = Guid.CreateVersion7(),
        BuyerId = Guid.CreateVersion7(),
        PaymentMethodId = Guid.CreateVersion7(),
        Currency = "USD",
        Items = [new CreateOrderItemInput(Guid.CreateVersion7(), "SKU", "Name", 1, 1m)],
        ShippingAddress = new AddressInput("1", null, "City", null, "11000", "CZ"),
        BillingAddress = new AddressInput("2", null, "City", null, "11000", "CZ"),
        RequestedAtUtc = TestAggregate.UtcNow,
    };

    [Fact]
    public void Validate_Happy_HasNoErrors()
    {
        _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyItems_Fails()
    {
        var c = Valid() with { Items = [] };
        _validator.TestValidate(c).ShouldHaveValidationErrorFor(x => x.Items);
    }

    [Fact]
    public void Validate_EmptyOrderId_Fails()
    {
        var c = Valid() with { OrderId = Guid.Empty };
        _validator.TestValidate(c).ShouldHaveValidationErrorFor(x => x.OrderId);
    }

    [Fact]
    public void Validate_LowercaseCurrency_Fails()
    {
        var c = Valid() with { Currency = "usd" };
        _validator.TestValidate(c).ShouldHaveValidationErrorFor(x => x.Currency);
    }

    [Fact]
    public void Validate_NegativeQuantity_FailsOnItem()
    {
        var c = Valid() with { Items = [new CreateOrderItemInput(Guid.CreateVersion7(), "S", "N", 0, 1m)] };
        _validator.TestValidate(c).ShouldHaveValidationErrorFor("Items[0].Quantity");
    }

    [Fact]
    public void Validate_ThreeLetterLowercaseCountryCode_Fails()
    {
        var c = Valid() with { ShippingAddress = new AddressInput("1", null, "City", null, "11000", "cz") };
        _validator.TestValidate(c).ShouldHaveValidationErrorFor("ShippingAddress.CountryCode");
    }
}
