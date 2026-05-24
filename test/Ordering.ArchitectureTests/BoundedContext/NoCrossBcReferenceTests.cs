using NetArchTest.Rules;

namespace Ordering.ArchitectureTests.BoundedContext;

public sealed class NoCrossBcReferenceTests : BaseTest
{
    private static readonly string[] OtherBcNamespacePrefixes =
    [
        "Basket.Domain", "Basket.Application",
        "Catalog.Domain", "Catalog.Application",
        "Inventory.Domain", "Inventory.Application",
        "Invoicing.Domain", "Invoicing.Application",
        "Payments.Domain", "Payments.Application",
    ];

    [Fact]
    public void OrderingDomain_Should_NotReference_OtherBcs()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOnAny(OtherBcNamespacePrefixes)
            .GetResult();
        result.FailingTypes.Should().BeEmpty(
            "Cross-BC integration only via Avro events / HTTP ACL adapters (architecture-tests.md § 1.6)");
    }

    [Fact]
    public void OrderingApplication_Should_NotReference_OtherBcs()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOnAny(OtherBcNamespacePrefixes)
            .GetResult();
        result.FailingTypes.Should().BeEmpty();
    }
}
