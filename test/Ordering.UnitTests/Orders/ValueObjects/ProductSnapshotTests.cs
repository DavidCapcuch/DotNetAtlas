using FluentResults.Extensions.FluentAssertions;
using Ordering.Domain.Orders.ValueObjects;
using Platform.SharedKernel.Errors;

namespace Ordering.UnitTests.Orders.ValueObjects;

public class ProductSnapshotTests
{
    [Fact]
    public void Create_Valid_TrimsAndReturnsSnapshot()
    {
        var result = ProductSnapshot.Create("  SKU-001  ", "  Widget  ");

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Sku.Should().Be("SKU-001");
            result.Value.Name.Should().Be("Widget");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptySku_ReturnsSkuEmptyError(string? sku)
    {
        var result = ProductSnapshot.Create(sku, "Widget");

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "ProductSnapshot.SkuEmpty");
        }
    }

    [Fact]
    public void Create_SkuTooLong_ReturnsSkuTooLongError()
    {
        var result = ProductSnapshot.Create(new string('x', ProductSnapshot.MaxSkuLength + 1), "Widget");

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "ProductSnapshot.SkuTooLong");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyName_ReturnsNameEmptyError(string? name)
    {
        var result = ProductSnapshot.Create("SKU-001", name);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "ProductSnapshot.NameEmpty");
        }
    }

    [Fact]
    public void Create_NameTooLong_ReturnsNameTooLongError()
    {
        var result = ProductSnapshot.Create("SKU-001", new string('x', ProductSnapshot.MaxNameLength + 1));

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "ProductSnapshot.NameTooLong");
        }
    }
}
