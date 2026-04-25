using NetArchTest.Rules;
using Platform.SharedKernel.Base;

namespace Catalog.ArchitectureTests.BoundedContext;

/// <summary>
/// Per architecture-tests.md § 2.1, the <c>Product</c> aggregate references the <c>Category</c>
/// aggregate solely by ID — never by type. Direct type references would invite navigation-property
/// joins that erode the aggregate boundary and break the single-aggregate-per-transaction rule.
/// </summary>
public class ProductTests : BaseTest
{
    /// <summary>
    /// Stricter than the generic cross-aggregate dependency check in <c>AggregateRootTests</c>:
    /// scans <c>Product</c>'s fields, properties, method parameters, and return types directly
    /// for any reference to <c>Catalog.Domain.Categories.Category</c>. A future contributor
    /// adding e.g. <c>private readonly Category _category</c> would fail here even if the
    /// dependency-graph check coincidentally found another path.
    /// </summary>
    [Fact]
    public void Product_ShouldNot_ReferenceCategoryType_OnFieldsPropertiesOrParameters()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .Inherit(typeof(AggregateRoot<>))
            .And().HaveName("Product")
            .Should()
            .MeetCustomRule(new OnlyReferencesByIdRule(typeof(global::Catalog.Domain.Categories.Category)))
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Product must reference Category only via the CategoryId Guid VO — never the Category " +
            "aggregate type — to keep aggregate boundaries crisp (one aggregate per transaction).");
    }
}
