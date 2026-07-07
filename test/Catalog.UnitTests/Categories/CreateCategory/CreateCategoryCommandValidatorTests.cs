using Catalog.Application.Categories.CreateCategory;

namespace Catalog.UnitTests.Categories.CreateCategory;

public class CreateCategoryCommandValidatorTests
{
    private readonly CreateCategoryCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidRootCategory_Passes()
    {
        // Arrange
        var cmd = new CreateCategoryCommand { Name = "Electronics", ParentCategoryId = null };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ValidChildCategory_Passes()
    {
        // Arrange
        var cmd = new CreateCategoryCommand
        {
            Name = "Laptops",
            ParentCategoryId = Guid.CreateVersion7(),
        };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyName_Fails()
    {
        // Arrange
        var cmd = new CreateCategoryCommand { Name = string.Empty };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_NameOver100Chars_Fails()
    {
        // Arrange
        var cmd = new CreateCategoryCommand { Name = new string('x', 101) };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    // CAT-SEC-006 / #188 followup: counts Unicode scalars, not UTF-16 code units, so a
    // non-BMP rune (𝓪 / U+1D4EA, 2 chars per appearance) is treated as a single rune.
    [Fact]
    public void Validate_NameOf100EmojiRunes_PassesRuneCheck()
    {
        // Arrange
        var name = string.Concat(Enumerable.Repeat("𝓪", 100));
        var cmd = new CreateCategoryCommand { Name = name };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_NameOf101EmojiRunes_FailsRuneCheck()
    {
        // Arrange
        var name = string.Concat(Enumerable.Repeat("𝓪", 101));
        var cmd = new CreateCategoryCommand { Name = name };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_EmptyGuidParent_Fails()
    {
        // Arrange
        var cmd = new CreateCategoryCommand
        {
            Name = "x",
            ParentCategoryId = Guid.Empty,
        };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }
}
