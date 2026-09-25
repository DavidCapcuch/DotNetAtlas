using NetArchTest.Rules;

namespace Notifications.ArchitectureTests.BoundedContext;

/// <summary>
/// No direct type reference from <c>Notifications.Domain</c> / <c>Notifications.Application</c> to
/// another BC's domain or application assemblies (architecture-tests.md § 1.6, which also sets the
/// Domain + Application scope).
/// </summary>
public class CrossBcReferenceTests : BaseTest
{
    private static readonly string[] OtherBcAssemblies =
    [
        "Basket.Domain",
        "Basket.Application",
        "Catalog.Domain",
        "Catalog.Application",
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
    public void NotificationsDomain_ShouldNot_ReferenceOtherBoundedContexts()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOnAny(OtherBcAssemblies)
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Notifications.Domain must not reference any other BC's Domain or Application assembly. " +
            "Cross-BC integration goes through Avro events (outbox/inbox).");
    }

    [Fact]
    public void NotificationsApplication_ShouldNot_ReferenceOtherBoundedContexts()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOnAny(OtherBcAssemblies)
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Notifications.Application must not reference any other BC's Domain or Application assembly. " +
            "Cross-BC integration goes through Avro events (outbox/inbox).");
    }
}
