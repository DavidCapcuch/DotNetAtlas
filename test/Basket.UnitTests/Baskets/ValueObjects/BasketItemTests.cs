using Basket.Domain.Baskets.ValueObjects;
using FluentResults.Extensions.FluentAssertions;
using Platform.SharedKernel.Errors;

namespace Basket.UnitTests.Baskets.ValueObjects;

public class BasketItemTests
{
    [Fact]
    public void Create_WithValidInputs_ReturnsItem()
    {
        var productId = Guid.CreateVersion7();
        var snapshot = BasketTestData.Snapshot();

        var result = BasketItem.Create(productId, snapshot, quantity: 2);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.ProductId.Should().Be(productId);
            result.Value.Snapshot.Should().Be(snapshot);
            result.Value.Quantity.Should().Be(2);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-999)]
    public void Create_WithNonPositiveQuantity_FailsWithInvalidQuantity(int quantity)
    {
        var result = BasketItem.Create(Guid.CreateVersion7(), BasketTestData.Snapshot(), quantity);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            var err = result.Errors[0].Should().BeOfType<ValidationError>().Subject;
            err.ErrorCode.Should().Be("BasketItem.InvalidQuantity");
        }
    }

    [Fact]
    public void StructuralEquality_DifferentInstancesWithSameValues_AreEqual()
    {
        var productId = Guid.CreateVersion7();
        var snapshot = BasketTestData.Snapshot();

        var a = BasketItem.Create(productId, snapshot, 3).Value;
        var b = BasketItem.Create(productId, snapshot, 3).Value;

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void StructuralEquality_DifferentQuantity_AreNotEqual()
    {
        var productId = Guid.CreateVersion7();
        var snapshot = BasketTestData.Snapshot();

        BasketItem.Create(productId, snapshot, 1).Value
            .Should().NotBe(BasketItem.Create(productId, snapshot, 2).Value);
    }
}
