using NetArchTest.Rules;

namespace Catalog.ArchitectureTests.BoundedContext;

/// <summary>
/// Per architecture-tests.md § 1.6, no direct type reference from <c>Catalog.Domain</c> /
/// <c>Catalog.Application</c> to another BC's domain or application assemblies. Cross-BC
/// integration only happens via Avro events through the inbox/outbox or HTTP ACL adapters in
/// Infrastructure. Catalog.Infrastructure intentionally consumes <c>Avro/Inventory/Stock/StockLevelChanged</c>
/// via <c>Platform.SchemaRegistry.Contracts</c> (M4.2 inbox), so the rule is scoped to Domain +
/// Application only.
/// </summary>
public class CrossBcReferenceTests : BaseTest
{
    private static readonly string[] OtherBcAssemblies =
    [
        "Basket.Domain",
        "Basket.Application",
        "Ordering.Domain",
        "Ordering.Application",
        "Inventory.Domain",
        "Inventory.Application",
        "Invoicing.Domain",
        "Invoicing.Application",
        "Payments.Domain",
        "Payments.Application",
    ];

    [Fact]
    public void CatalogDomain_ShouldNot_ReferenceOtherBoundedContexts()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOnAny(OtherBcAssemblies)
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Catalog.Domain must not reference any other BC's Domain or Application assembly. " +
            "Cross-BC integration goes through Avro events (outbox/inbox) or HTTP ACL adapters.");
    }

    [Fact]
    public void CatalogApplication_ShouldNot_ReferenceOtherBoundedContexts()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOnAny(OtherBcAssemblies)
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Catalog.Application must not reference any other BC's Domain or Application assembly. " +
            "Cross-BC integration goes through Avro events (outbox/inbox) or HTTP ACL adapters.");
    }
}
