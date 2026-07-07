using Catalog.Application.Products.DiscontinueProduct;

namespace Catalog.UnitTests.Products.DiscontinueProduct;

public class DiscontinueProductCommandValidatorTests
{
    private readonly DiscontinueProductCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidCommand_Passes()
    {
        // Arrange
        var cmd = new DiscontinueProductCommand
        {
            ProductId = Guid.CreateVersion7(),
            Reason = "Supplier exited",
        };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyReason_Fails()
    {
        // Arrange
        var cmd = new DiscontinueProductCommand
        {
            ProductId = Guid.CreateVersion7(),
            Reason = string.Empty,
        };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ReasonOver500Chars_Fails()
    {
        // Arrange
        var cmd = new DiscontinueProductCommand
        {
            ProductId = Guid.CreateVersion7(),
            Reason = new string('x', 501),
        };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    // CAT-SEC-006 / #188 followup: counts Unicode scalars, not UTF-16 code units, so a
    // non-BMP rune (𝓪 / U+1D4EA, 2 chars per appearance) is treated as a single rune.
    [Fact]
    public void Validate_ReasonOf500EmojiRunes_PassesRuneCheck()
    {
        // Arrange
        var cmd = new DiscontinueProductCommand
        {
            ProductId = Guid.CreateVersion7(),
            Reason = string.Concat(Enumerable.Repeat("𝓪", 500)),
        };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ReasonOf501EmojiRunes_FailsRuneCheck()
    {
        // Arrange
        var cmd = new DiscontinueProductCommand
        {
            ProductId = Guid.CreateVersion7(),
            Reason = string.Concat(Enumerable.Repeat("𝓪", 501)),
        };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }
}
