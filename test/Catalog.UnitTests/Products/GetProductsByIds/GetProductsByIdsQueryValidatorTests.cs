using Catalog.Application.Products.GetProductsByIds;

namespace Catalog.UnitTests.Products.GetProductsByIds;

public class GetProductsByIdsQueryValidatorTests
{
    private readonly GetProductsByIdsQueryValidator _validator = new();

    [Fact]
    public void Valid_single_id_passes()
    {
        var q = new GetProductsByIdsQuery { Ids = [Guid.CreateVersion7()] };
        _validator.Validate(q).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_list_fails()
    {
        _validator.Validate(new GetProductsByIdsQuery { Ids = Array.Empty<Guid>() }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Over_100_ids_fails()
    {
        var ids = Enumerable.Range(0, 101).Select(_ => Guid.CreateVersion7()).ToList();
        _validator.Validate(new GetProductsByIdsQuery { Ids = ids }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Empty_guid_in_list_fails()
    {
        _validator.Validate(new GetProductsByIdsQuery { Ids = [Guid.Empty] }).IsValid.Should().BeFalse();
    }
}
