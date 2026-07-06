using System.Reflection;
using Mono.Cecil;
using NetArchTest.Rules;

namespace EShop.BFF.ArchitectureTests.CleanArchitecture;

/// <summary>
/// Layer + statelessness rules for the 2-layer aggregation gateway (bff.md § 1 + § 7): Api → Infra
/// (one-way), no persistence and no Kafka <em>producer</em> (the BFF owns no state and produces no
/// events — it only consumes for cache invalidation, bff.md § 2.2), and no direct reference to any
/// upstream bounded-context assembly (anti-corruption — upstream contracts are re-declared as
/// BFF-internal DTOs; the Avro records it consumes ship from <c>Platform.SchemaRegistry.Contracts</c>,
/// not a BC assembly).
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
    public void Bff_IsStateless_NoEntityFramework_AndConsumesOnly_NoKafkaProducer()
    {
        // The BFF owns no database (no EF) and produces no events — its only Kafka usage is the
        // bff-group consumer, so it must not depend on the producer API (bff.md § 2.2 / § 7).
        var result = Types.InAssembly(InfrastructureAssembly)
            .Should()
            .NotHaveDependencyOnAny("Microsoft.EntityFrameworkCore", "KafkaFlow.Producers")
            .GetResult();

        result.FailingTypes.Should().BeNullOrEmpty(
            "the BFF owns no database and produces no events — no DbSet, no Kafka producer (bff.md § 7): {0}",
            string.Join(", ", result.FailingTypes?.Select(t => t.Name) ?? []));
    }

    [Fact]
    public void Bff_DoesNotReference_AnyBoundedContextAssembly()
    {
        // Checked at the assembly-reference level (not by namespace): the BFF consumes Avro records that
        // live in BC-named namespaces (Catalog.Products, Inventory.Stock, …) but ship from the
        // Platform.SchemaRegistry.Contracts assembly — those are allowed; a real BC assembly is not.
        using (new AssertionScope())
        {
            AssertReferencesNoBoundedContextAssembly(ApiAssembly);
            AssertReferencesNoBoundedContextAssembly(InfrastructureAssembly);
        }
    }

    private static void AssertReferencesNoBoundedContextAssembly(Assembly assembly)
    {
        using var module = ModuleDefinition.ReadModule(assembly.Location);

        var offending = module.AssemblyReferences
            .Select(reference => reference.Name)
            .Where(name => BoundedContextAssemblyPrefixes.Any(prefix =>
                name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();

        offending.Should().BeEmpty(
            "{0} must re-declare upstream contracts as BFF-internal DTOs and consume Avro only from " +
            "Platform.SchemaRegistry.Contracts, never reference a BC assembly (bff.md § 1)",
            assembly.GetName().Name);
    }
}
