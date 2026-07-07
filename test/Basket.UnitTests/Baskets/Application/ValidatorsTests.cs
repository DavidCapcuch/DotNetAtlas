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
    public void AddItem_WhenValid_Passes()
    {
        // Arrange
        var validator = new AddItemToBasketCommandValidator();
        var command = new AddItemToBasketCommand(UserId, ProductId, 1);

        // Act
        var result = validator.Validate(command);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1001)]
    [Trait("Category", "boundary")]
    public void AddItem_WhenQuantityOutOfRange_Fails(int quantity)
    {
        // Arrange
        var validator = new AddItemToBasketCommandValidator();
        var command = new AddItemToBasketCommand(UserId, ProductId, quantity);

        // Act
        var result = validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void AddItem_WhenEmptyUserId_Fails()
    {
        // Arrange
        var validator = new AddItemToBasketCommandValidator();
        var command = new AddItemToBasketCommand(Guid.Empty, ProductId, 1);

        // Act
        var result = validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void AddItem_WhenEmptyProductId_Fails()
    {
        // Arrange
        var validator = new AddItemToBasketCommandValidator();
        var command = new AddItemToBasketCommand(UserId, Guid.Empty, 1);

        // Act
        var result = validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // RemoveItemFromBasketCommandValidator
    // ------------------------------------------------------------------

    [Fact]
    public void RemoveItem_WhenValid_Passes()
    {
        // Arrange
        var validator = new RemoveItemFromBasketCommandValidator();
        var command = new RemoveItemFromBasketCommand(UserId, ProductId);

        // Act
        var result = validator.Validate(command);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void RemoveItem_WhenEmptyIds_Fails()
    {
        // Arrange
        var validator = new RemoveItemFromBasketCommandValidator();
        var command = new RemoveItemFromBasketCommand(Guid.Empty, Guid.Empty);

        // Act
        var result = validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // ChangeItemQuantityCommandValidator
    // ------------------------------------------------------------------

    [Fact]
    public void ChangeItemQuantity_WhenValid_Passes()
    {
        // Arrange
        var validator = new ChangeItemQuantityCommandValidator();
        var command = new ChangeItemQuantityCommand(UserId, ProductId, 5);

        // Act
        var result = validator.Validate(command);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    [Trait("Category", "boundary")]
    public void ChangeItemQuantity_WhenQuantityOutOfRange_Fails(int newQuantity)
    {
        // Arrange
        var validator = new ChangeItemQuantityCommandValidator();
        var command = new ChangeItemQuantityCommand(UserId, ProductId, newQuantity);

        // Act
        var result = validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // RefreshBasketPricesCommandValidator / ClearBasketCommandValidator
    // ------------------------------------------------------------------

    [Fact]
    public void RefreshPrices_WhenValid_Passes()
    {
        // Arrange
        var validator = new RefreshBasketPricesCommandValidator();
        var command = new RefreshBasketPricesCommand(UserId);

        // Act
        var result = validator.Validate(command);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void RefreshPrices_WhenEmptyUserId_Fails()
    {
        // Arrange
        var validator = new RefreshBasketPricesCommandValidator();
        var command = new RefreshBasketPricesCommand(Guid.Empty);

        // Act
        var result = validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Clear_WhenEmptyUserId_Fails()
    {
        // Arrange
        var validator = new ClearBasketCommandValidator();
        var command = new ClearBasketCommand(Guid.Empty);

        // Act
        var result = validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void GetByUserId_WhenEmptyUserId_Fails()
    {
        // Arrange
        var validator = new GetBasketByUserIdQueryValidator();
        var query = new GetBasketByUserIdQuery(Guid.Empty);

        // Act
        var result = validator.Validate(query);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // CheckoutBasketCommandValidator
    // ------------------------------------------------------------------

    [Fact]
    public void Checkout_WhenValid_Passes()
    {
        // Arrange
        var validator = new CheckoutBasketCommandValidator();
        var command = new CheckoutBasketCommand(
            UserId,
            ApplicationTestData.AddressDto(),
            ApplicationTestData.AddressDto(),
            Guid.CreateVersion7());

        // Act
        var result = validator.Validate(command);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Checkout_WhenEmptyPaymentMethodId_Fails()
    {
        // Arrange
        var validator = new CheckoutBasketCommandValidator();
        var command = new CheckoutBasketCommand(
            UserId,
            ApplicationTestData.AddressDto(),
            ApplicationTestData.AddressDto(),
            Guid.Empty);

        // Act
        var result = validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("us")] // lowercase
    [InlineData("USA")] // 3 chars
    [InlineData("")]
    public void Checkout_WhenInvalidCountryCode_Fails(string countryCode)
    {
        // Arrange
        var validator = new CheckoutBasketCommandValidator();
        var command = new CheckoutBasketCommand(
            UserId,
            new CheckoutAddressDto
            {
                Street1 = "S",
                City = "C",
                PostalCode = "P",
                CountryCode = countryCode,
            },
            ApplicationTestData.AddressDto(),
            Guid.CreateVersion7());

        // Act
        var result = validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Checkout_WhenEmptyShippingStreet1_Fails()
    {
        // Arrange
        var validator = new CheckoutBasketCommandValidator();
        var command = new CheckoutBasketCommand(
            UserId,
            new CheckoutAddressDto
            {
                Street1 = "",
                City = "C",
                PostalCode = "P",
                CountryCode = "US",
            },
            ApplicationTestData.AddressDto(),
            Guid.CreateVersion7());

        // Act
        var result = validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "boundary")]
    public void Checkout_WhenStreet1OverMaxLength_Fails()
    {
        // Arrange
        var validator = new CheckoutBasketCommandValidator();
        var command = new CheckoutBasketCommand(
            UserId,
            new CheckoutAddressDto
            {
                Street1 = new string('a', 201), // exceeds Address.Create's 200-char ceiling
                City = "C",
                PostalCode = "P",
                CountryCode = "US",
            },
            ApplicationTestData.AddressDto(),
            Guid.CreateVersion7());

        // Act
        var result = validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
    }
}
