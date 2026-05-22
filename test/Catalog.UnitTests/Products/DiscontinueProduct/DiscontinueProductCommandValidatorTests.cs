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

    // CAT-SEC-006 / #188 followup: counts Unicode scalars, not UTF-16 code units, so a
    // non-BMP rune (𝓪 / U+1D4EA, 2 chars per appearance) is treated as a single rune.
    [Fact]
    public void Reason_of_500_emoji_runes_passes_rune_check()
    {
        var cmd = new DiscontinueProductCommand
        {
            ProductId = Guid.CreateVersion7(),
            Reason = string.Concat(Enumerable.Repeat("𝓪", 500)),
        };

        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Reason_of_501_emoji_runes_fails_rune_check()
    {
        var cmd = new DiscontinueProductCommand
        {
            ProductId = Guid.CreateVersion7(),
            Reason = string.Concat(Enumerable.Repeat("𝓪", 501)),
        };

        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }
}
