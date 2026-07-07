using Catalog.Application.Products.GetProductsByIds;

namespace Catalog.UnitTests.Products.GetProductsByIds;

public class GetProductsByIdsQueryValidatorTests
{
    private readonly GetProductsByIdsQueryValidator _validator = new();

    [Fact]
    public void Validate_SingleId_Passes()
    {
        // Arrange
        var q = new GetProductsByIdsQuery { Ids = [Guid.CreateVersion7()] };

        // Act & Assert
        _validator.Validate(q).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyList_Fails()
    {
        // Act & Assert
        _validator.Validate(new GetProductsByIdsQuery { Ids = Array.Empty<Guid>() }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_Over100Ids_Fails()
    {
        // Arrange
        var ids = Enumerable.Range(0, 101).Select(_ => Guid.CreateVersion7()).ToList();

        // Act & Assert
        _validator.Validate(new GetProductsByIdsQuery { Ids = ids }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_EmptyGuidInList_Fails()
    {
        // Act & Assert
        _validator.Validate(new GetProductsByIdsQuery { Ids = [Guid.Empty] }).IsValid.Should().BeFalse();
    }
}
