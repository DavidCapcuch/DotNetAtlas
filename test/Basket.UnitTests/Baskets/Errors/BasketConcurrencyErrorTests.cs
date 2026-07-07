using Basket.Domain.Baskets.Errors;
using Platform.SharedKernel.Errors;

namespace Basket.UnitTests.Baskets.Errors;

public class BasketConcurrencyErrorTests
{
    [Fact]
    public void Constructor_PopulatesFieldsAndCanonicalShape()
    {
        // Arrange
        var userId = Guid.CreateVersion7();

        // Act
        var error = new BasketConcurrencyError(userId, expected: 3, actual: 5);

        // Assert
        using (new AssertionScope())
        {
            error.Should().BeAssignableTo<ConflictError>();
            error.EntityName.Should().Be("Basket");
            error.ErrorCode.Should().Be("Basket.Concurrency");
            error.UserId.Should().Be(userId);
            error.Expected.Should().Be(3);
            error.Actual.Should().Be(5);
            error.Message.Should().Contain(userId.ToString());
            error.Message.Should().Contain("expected 3");
            error.Message.Should().Contain("found 5");
            error.Reasons.Should().BeEmpty();
        }
    }
}
