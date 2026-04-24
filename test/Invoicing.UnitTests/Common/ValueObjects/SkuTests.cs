using Invoicing.Domain.Common.ValueObjects;

namespace Invoicing.UnitTests.Common.ValueObjects;

public class SkuTests
{
    [Theory]
    [InlineData("WIDGET-001")]
    [InlineData("A")]
    public void Create_AcceptsValid(string value)
    {
        Sku.Create(value).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsEmptyOrWhitespace(string value)
    {
        Sku.Create(value).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Create_RejectsOverMaxLength()
    {
        var tooLong = new string('x', Sku.MaxLength + 1);
        Sku.Create(tooLong).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Create_TrimsWhitespace()
    {
        Sku.Create("  SKU-1  ").Value.Value.Should().Be("SKU-1");
    }
}
