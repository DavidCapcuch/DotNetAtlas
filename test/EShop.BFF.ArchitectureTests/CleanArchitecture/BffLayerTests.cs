using NetArchTest.Rules;

namespace EShop.BFF.ArchitectureTests.CleanArchitecture;

/// <summary>
/// Layer + statelessness rules for the 2-layer aggregation gateway (bff.md § 1 + § 7): Api → Infra
/// (one-way), no persistence or messaging stack (the BFF owns no state and produces no events), and
/// no direct reference to any upstream bounded-context assembly (anti-corruption — upstream contracts
/// are re-declared as BFF-internal DTOs).
/// </summary>
public sealed class BffLayerTests : BaseTest
{
    private static readonly string[] BoundedContextAssemblyPrefixes =
    [
        "Catalog.", "Basket.", "Ordering.", "Inventory.", "Payments.", "Invoicing.", "Notifications.",
    ];

    [Fact]
    public void Infrastructure_ShouldNotDependOn_Api()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .Should()
            .NotHaveDependencyOnAny(ApiAssembly.GetName().Name!)
            .GetResult();

        result.FailingTypes.Should().BeNullOrEmpty(
            "Infrastructure must not depend on Api (bff.md § 1)");
    }

    [Fact]
    public void Bff_IsStateless_NoEntityFrameworkOrKafka()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .Should()
            .NotHaveDependencyOnAny("Microsoft.EntityFrameworkCore", "KafkaFlow")
            .GetResult();

        result.FailingTypes.Should().BeNullOrEmpty(
            "the BFF owns no database and produces no events — no DbSet, no Kafka producer (bff.md § 7): {0}",
            string.Join(", ", result.FailingTypes?.Select(t => t.Name) ?? []));
    }

    [Fact]
    public void Bff_DoesNotReference_AnyBoundedContextAssembly()
    {
        var apiResult = Types.InAssembly(ApiAssembly)
            .Should()
            .NotHaveDependencyOnAny(BoundedContextAssemblyPrefixes)
            .GetResult();

        var infrastructureResult = Types.InAssembly(InfrastructureAssembly)
            .Should()
            .NotHaveDependencyOnAny(BoundedContextAssemblyPrefixes)
            .GetResult();

        using var _ = new AssertionScope();
        apiResult.FailingTypes.Should().BeNullOrEmpty(
            "Api must re-declare upstream contracts as BFF-internal DTOs, never reference a BC assembly (bff.md § 1)");
        infrastructureResult.FailingTypes.Should().BeNullOrEmpty(
            "Infrastructure must re-declare upstream contracts as BFF-internal DTOs, never reference a BC assembly (bff.md § 1)");
    }
}
