using Basket.Application.Baskets.AddItem;
using Basket.Application.Baskets.ChangeItemQuantity;
using Basket.Application.Baskets.Checkout;
using Basket.Application.Baskets.Clear;
using Basket.Application.Baskets.Common.Contracts;
using Basket.Application.Baskets.GetByUserId;
using Basket.Application.Baskets.RefreshPrices;
using Basket.Application.Baskets.RemoveItem;

namespace Basket.UnitTests.Baskets.Application;

/// <summary>
/// Table-driven validator coverage for all Basket command + query validators.
/// </summary>
public class ValidatorsTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid ProductId = Guid.CreateVersion7();

    // ------------------------------------------------------------------
    // AddItemToBasketCommandValidator
    // ------------------------------------------------------------------

    [Fact]
    public void AddItem_Valid_Passes()
    {
        var v = new AddItemToBasketCommandValidator();
        v.Validate(new AddItemToBasketCommand(UserId, ProductId, 1)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1001)]
    public void AddItem_InvalidQuantity_Fails(int quantity)
    {
        var v = new AddItemToBasketCommandValidator();
        v.Validate(new AddItemToBasketCommand(UserId, ProductId, quantity)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void AddItem_EmptyUserId_Fails()
    {
        var v = new AddItemToBasketCommandValidator();
        v.Validate(new AddItemToBasketCommand(Guid.Empty, ProductId, 1)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void AddItem_EmptyProductId_Fails()
    {
        var v = new AddItemToBasketCommandValidator();
        v.Validate(new AddItemToBasketCommand(UserId, Guid.Empty, 1)).IsValid.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // RemoveItemFromBasketCommandValidator
    // ------------------------------------------------------------------

    [Fact]
    public void RemoveItem_Valid_Passes()
    {
        var v = new RemoveItemFromBasketCommandValidator();
        v.Validate(new RemoveItemFromBasketCommand(UserId, ProductId)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void RemoveItem_EmptyIds_Fails()
    {
        var v = new RemoveItemFromBasketCommandValidator();
        v.Validate(new RemoveItemFromBasketCommand(Guid.Empty, Guid.Empty)).IsValid.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // ChangeItemQuantityCommandValidator
    // ------------------------------------------------------------------

    [Fact]
    public void ChangeItemQuantity_Valid_Passes()
    {
        var v = new ChangeItemQuantityCommandValidator();
        v.Validate(new ChangeItemQuantityCommand(UserId, ProductId, 5)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void ChangeItemQuantity_OutOfRange_Fails(int qty)
    {
        var v = new ChangeItemQuantityCommandValidator();
        v.Validate(new ChangeItemQuantityCommand(UserId, ProductId, qty)).IsValid.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // RefreshBasketPricesCommandValidator / ClearBasketCommandValidator
    // ------------------------------------------------------------------

    [Fact]
    public void RefreshPrices_Valid_Passes()
    {
        new RefreshBasketPricesCommandValidator()
            .Validate(new RefreshBasketPricesCommand(UserId)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void RefreshPrices_EmptyUserId_Fails()
    {
        new RefreshBasketPricesCommandValidator()
            .Validate(new RefreshBasketPricesCommand(Guid.Empty)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Clear_EmptyUserId_Fails()
    {
        new ClearBasketCommandValidator()
            .Validate(new ClearBasketCommand(Guid.Empty)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void GetByUserId_EmptyUserId_Fails()
    {
        new GetBasketByUserIdQueryValidator()
            .Validate(new GetBasketByUserIdQuery(Guid.Empty)).IsValid.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // CheckoutBasketCommandValidator
    // ------------------------------------------------------------------

    [Fact]
    public void Checkout_Valid_Passes()
    {
        var v = new CheckoutBasketCommandValidator();
        var cmd = new CheckoutBasketCommand(
            UserId,
            Guid.CreateVersion7(),
            ApplicationTestData.AddressDto(),
            ApplicationTestData.AddressDto(),
            Guid.CreateVersion7());

        v.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Checkout_NonVersion7CorrelationId_Fails()
    {
        var v = new CheckoutBasketCommandValidator();
        var v4 = Guid.NewGuid(); // Version 4
        var cmd = new CheckoutBasketCommand(
            UserId,
            v4,
            ApplicationTestData.AddressDto(),
            ApplicationTestData.AddressDto(),
            Guid.CreateVersion7());

        v.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Checkout_EmptyCorrelationId_Fails()
    {
        var v = new CheckoutBasketCommandValidator();
        var cmd = new CheckoutBasketCommand(
            UserId,
            Guid.Empty,
            ApplicationTestData.AddressDto(),
            ApplicationTestData.AddressDto(),
            Guid.CreateVersion7());

        v.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Checkout_EmptyPaymentMethodId_Fails()
    {
        var v = new CheckoutBasketCommandValidator();
        var cmd = new CheckoutBasketCommand(
            UserId,
            Guid.CreateVersion7(),
            ApplicationTestData.AddressDto(),
            ApplicationTestData.AddressDto(),
            Guid.Empty);

        v.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("us")] // lowercase
    [InlineData("USA")] // 3 chars
    [InlineData("")]
    public void Checkout_InvalidCountryCode_Fails(string countryCode)
    {
        var v = new CheckoutBasketCommandValidator();
        var cmd = new CheckoutBasketCommand(
            UserId,
            Guid.CreateVersion7(),
            new CheckoutAddressDto
            {
                Street1 = "S",
                City = "C",
                PostalCode = "P",
                CountryCode = countryCode,
            },
            ApplicationTestData.AddressDto(),
            Guid.CreateVersion7());

        v.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Checkout_EmptyShippingStreet1_Fails()
    {
        var v = new CheckoutBasketCommandValidator();
        var cmd = new CheckoutBasketCommand(
            UserId,
            Guid.CreateVersion7(),
            new CheckoutAddressDto
            {
                Street1 = "",
                City = "C",
                PostalCode = "P",
                CountryCode = "US",
            },
            ApplicationTestData.AddressDto(),
            Guid.CreateVersion7());

        v.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Checkout_Street1OverMaxLength_Fails()
    {
        var v = new CheckoutBasketCommandValidator();
        var cmd = new CheckoutBasketCommand(
            UserId,
            Guid.CreateVersion7(),
            new CheckoutAddressDto
            {
                Street1 = new string('a', 201), // exceeds Address.Create's 200-char ceiling
                City = "C",
                PostalCode = "P",
                CountryCode = "US",
            },
            ApplicationTestData.AddressDto(),
            Guid.CreateVersion7());

        v.Validate(cmd).IsValid.Should().BeFalse();
    }
}
