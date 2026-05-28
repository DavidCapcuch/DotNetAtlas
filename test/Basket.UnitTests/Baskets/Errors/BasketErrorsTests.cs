using Basket.Domain.Baskets.Errors;
using Platform.SharedKernel.Errors;

namespace Basket.UnitTests.Baskets.Errors;

public class BasketErrorsTests
{
    [Fact]
    public void EmptyBasket_ReturnsConflictErrorWithEntityNameAndCode()
    {
        var error = BasketErrors.EmptyBasket();

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
        var error = BasketErrors.MaxItemsReached(max: 50);

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
        var error = BasketErrors.InvalidQuantity();

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
        var error = BasketErrors.CurrencyMismatch();

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
        var productId = Guid.CreateVersion7();

        var error = BasketErrors.ItemNotFound(productId);

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
        var userId = Guid.CreateVersion7();

        var error = BasketErrors.Corruption(userId);

        using (new AssertionScope())
        {
            error.Should().BeOfType<ValidationError>();
            error.PropertyName.Should().Be("Basket");
            error.ErrorCode.Should().Be("Basket.Corruption");
            error.Message.Should().Contain(userId.ToString());
        }
    }
}
