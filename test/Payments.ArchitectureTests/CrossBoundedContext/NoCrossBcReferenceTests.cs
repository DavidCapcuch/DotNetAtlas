using NetArchTest.Rules;

namespace Payments.ArchitectureTests.CrossBoundedContext;

/// <summary>
/// Per architecture-tests.md § 1.6, no direct type reference may cross BC boundaries from
/// Payments.Domain or Payments.Application. Cross-BC integration happens only via Avro events
/// consumed through the inbox (Payments owns the saga-command consumers under
/// Payments.Infrastructure) or via well-defined ACL adapters when synchronous reads are needed.
/// Payments has neither today — both Domain and Application stay isolated from peer BCs.
/// </summary>
public sealed class NoCrossBcReferenceTests : BaseTest
{
    private static readonly string[] OtherBcNamespacePrefixes =
    [
        "Basket.Domain", "Basket.Application",
        "Catalog.Domain", "Catalog.Application",
        "Inventory.Domain", "Inventory.Application",
        "Invoicing.Domain", "Invoicing.Application",
        "Ordering.Domain", "Ordering.Application",
    ];

    [Fact]
    public void PaymentsDomain_Should_NotReference_OtherBcs()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOnAny(OtherBcNamespacePrefixes)
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Cross-BC integration only via Avro events / HTTP ACL adapters (architecture-tests.md § 1.6)");
    }

    [Fact]
    public void PaymentsApplication_Should_NotReference_OtherBcs()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOnAny(OtherBcNamespacePrefixes)
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Cross-BC integration only via Avro events / HTTP ACL adapters (architecture-tests.md § 1.6)");
    }
}
