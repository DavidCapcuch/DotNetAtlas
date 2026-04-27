using NetArchTest.Rules;

namespace Inventory.ArchitectureTests.BoundedContext;

/// <summary>
/// No direct type reference from <c>Inventory.Domain</c> / <c>Inventory.Application</c> to
/// another BC's domain or application assemblies. Cross-BC integration only happens via Avro
/// events through the inbox/outbox or HTTP ACL adapters in Infrastructure. Inventory.Infrastructure
/// intentionally consumes <c>Avro/Catalog/ProductCreatedEvent</c> + <c>Avro/Ordering/OrderCancelledEvent</c>
/// via <c>Platform.SchemaRegistry.Contracts</c>, so this rule is scoped to Domain + Application only.
/// </summary>
public class CrossBcReferenceTests : BaseTest
{
    private static readonly string[] OtherBcAssemblies =
    [
        "Catalog.Domain",
        "Catalog.Application",
        "Basket.Domain",
        "Basket.Application",
        "Ordering.Domain",
        "Ordering.Application",
        "Invoicing.Domain",
        "Invoicing.Application",
        "Payments.Domain",
        "Payments.Application",
    ];

    [Fact]
    public void InventoryDomain_ShouldNot_ReferenceOtherBoundedContexts()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOnAny(OtherBcAssemblies)
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Inventory.Domain must not reference any other BC's Domain or Application assembly. " +
            "Cross-BC integration goes through Avro events (outbox/inbox) or HTTP ACL adapters.");
    }

    [Fact]
    public void InventoryApplication_ShouldNot_ReferenceOtherBoundedContexts()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOnAny(OtherBcAssemblies)
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Inventory.Application must not reference any other BC's Domain or Application assembly. " +
            "Cross-BC integration goes through Avro events (outbox/inbox) or HTTP ACL adapters.");
    }
}
