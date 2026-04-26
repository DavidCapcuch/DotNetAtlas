using NetArchTest.Rules;

namespace Basket.ArchitectureTests.CrossBC;

/// <summary>
/// Cross-BC isolation per architecture-tests.md § 1.6.
/// Basket.Domain and Basket.Application must not directly reference any other
/// bounded context's Domain or Application assemblies. Cross-BC integration
/// happens only via:
/// (1) Avro external events through the inbox (Platform.SchemaRegistry.Contracts), or
/// (2) HTTP ACL adapters in Basket.Infrastructure (e.g., ProductCatalogHttpAdapter).
/// </summary>
public class CrossBCReferenceTests : BaseTest
{
    private static readonly string[] ForbiddenBoundedContextAssemblies =
    [
        "Catalog.Domain",
        "Catalog.Application",
        "Inventory.Domain",
        "Inventory.Application",
        "Ordering.Domain",
        "Ordering.Application",
        "Invoicing.Domain",
        "Invoicing.Application",
        "Payments.Domain",
        "Payments.Application",
    ];

    [Fact]
    public void BasketDomain_DoesNotReference_OtherBoundedContexts()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOnAny(ForbiddenBoundedContextAssemblies)
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Basket.Domain must not reference any other bounded context's Domain/Application. " +
            "Cross-BC integration happens via Avro events (inbox) or HTTP ACL adapters (Infrastructure)");
    }

    [Fact]
    public void BasketApplication_DoesNotReference_OtherBoundedContexts()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOnAny(ForbiddenBoundedContextAssemblies)
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Basket.Application must not reference any other bounded context's Domain/Application. " +
            "Cross-BC integration happens via Avro events (inbox) or HTTP ACL adapters (Infrastructure)");
    }
}
