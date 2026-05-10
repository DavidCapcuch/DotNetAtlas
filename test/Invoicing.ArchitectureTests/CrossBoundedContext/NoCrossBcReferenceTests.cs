using NetArchTest.Rules;

namespace Invoicing.ArchitectureTests.CrossBoundedContext;

public sealed class NoCrossBcReferenceTests : BaseTest
{
    private static readonly string[] OtherBcNamespacePrefixes =
    [
        "Basket.Domain", "Basket.Application",
        "Catalog.Domain", "Catalog.Application",
        "Inventory.Domain", "Inventory.Application",
        "Ordering.Domain", "Ordering.Application",
        "Payments.Domain", "Payments.Application",
    ];

    [Fact]
    public void InvoicingDomain_Should_NotReference_OtherBcs()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOnAny(OtherBcNamespacePrefixes)
            .GetResult();
        result.FailingTypes.Should().BeEmpty(
            "Cross-BC integration only via Avro events / HTTP ACL adapters (architecture-tests.md § 1.6)");
    }

    [Fact]
    public void InvoicingApplication_Should_NotReference_OtherBcs()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOnAny(OtherBcNamespacePrefixes)
            .GetResult();
        result.FailingTypes.Should().BeEmpty();
    }
}
