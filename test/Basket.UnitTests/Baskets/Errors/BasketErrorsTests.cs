using Basket.Domain.Baskets.Errors;
using Platform.SharedKernel.Errors;

namespace Basket.UnitTests.Baskets.Errors;

public class BasketErrorsTests
{
    [Fact]
    public void EmptyBasket_ReturnsConflictErrorWithEntityNameAndCode()
    {
        // Act
        var error = BasketErrors.EmptyBasket();

        // Assert
        using (new AssertionScope())
        {
            error.Should().BeOfType<ConflictError>();
            error.EntityName.Should().Be("Basket");
            error.ErrorCode.Should().Be("Basket.Empty");
            error.Message.Should().Contain("at least one item");
        }
    }

    [Fact]
    public void MaxItemsReached_ReturnsConflictErrorWithEntityNameAndCode()
    {
        // Act
        var error = BasketErrors.MaxItemsReached(max: 50);

        // Assert
        using (new AssertionScope())
        {
            error.Should().BeOfType<ConflictError>();
            error.EntityName.Should().Be("Basket");
            error.ErrorCode.Should().Be("Basket.MaxItemsReached");
            error.Message.Should().Contain("50");
        }
    }

    [Fact]
    public void InvalidQuantity_ReturnsValidationErrorWithPropertyNameAndCode()
    {
        // Act
        var error = BasketErrors.InvalidQuantity();

        // Assert
        using (new AssertionScope())
        {
            error.Should().BeOfType<ValidationError>();
            error.PropertyName.Should().Be("Quantity");
            error.ErrorCode.Should().Be("Basket.InvalidQuantity");
        }
    }

    [Fact]
    public void CurrencyMismatch_ReturnsValidationErrorWithPropertyNameAndCode()
    {
        // Act
        var error = BasketErrors.CurrencyMismatch();

        // Assert
        using (new AssertionScope())
        {
            error.Should().BeOfType<ValidationError>();
            error.PropertyName.Should().Be("Currency");
            error.ErrorCode.Should().Be("Basket.CurrencyMismatch");
        }
    }

    [Fact]
    public void ItemNotFound_ReturnsNotFoundErrorWithEntityNameAndCode()
    {
        // Arrange
        var productId = Guid.CreateVersion7();

        // Act
        var error = BasketErrors.ItemNotFound(productId);

        // Assert
        using (new AssertionScope())
        {
            error.Should().BeOfType<NotFoundError>();
            error.EntityName.Should().Be("BasketItem");
            error.Id.Should().Be(productId);
            error.ErrorCode.Should().Be("Basket.ItemNotFound");
            error.Message.Should().Contain(productId.ToString());
        }
    }

    [Fact]
    public void Corruption_ReturnsValidationErrorWithPropertyNameAndCode()
    {
        // Arrange
        var userId = Guid.CreateVersion7();

        // Act
        var error = BasketErrors.Corruption(userId);

        // Assert
        using (new AssertionScope())
        {
            error.Should().BeOfType<ValidationError>();
            error.PropertyName.Should().Be("Basket");
            error.ErrorCode.Should().Be("Basket.Corruption");
            error.Message.Should().Contain(userId.ToString());
        }
    }
}
