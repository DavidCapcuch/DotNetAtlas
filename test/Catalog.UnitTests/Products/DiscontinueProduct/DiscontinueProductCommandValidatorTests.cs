using Catalog.Application.Products.DiscontinueProduct;

namespace Catalog.UnitTests.Products.DiscontinueProduct;

public class DiscontinueProductCommandValidatorTests
{
    private readonly DiscontinueProductCommandValidator _validator = new();

    [Fact]
    public void Valid_command_passes()
    {
        var cmd = new DiscontinueProductCommand
        {
            ProductId = Guid.CreateVersion7(),
            Reason = "Supplier exited",
        };

        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_reason_fails()
    {
        var cmd = new DiscontinueProductCommand
        {
            ProductId = Guid.CreateVersion7(),
            Reason = string.Empty,
        };

        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Reason_over_500_chars_fails()
    {
        var cmd = new DiscontinueProductCommand
        {
            ProductId = Guid.CreateVersion7(),
            Reason = new string('x', 501),
        };

        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }
}
