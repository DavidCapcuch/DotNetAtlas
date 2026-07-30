using Microsoft.EntityFrameworkCore;
using Mono.Cecil;
using NetArchTest.Rules;
using BasketAggregate = Basket.Domain.Baskets.Basket;

namespace Basket.ArchitectureTests.BasketSpecific;

/// <summary>
/// Basket-specific architecture rules per architecture-tests.md § 2.2 and basket.md
/// &lt;session_management&gt; line 148.
/// </summary>
public class BasketSpecificRulesTests : BaseTest
{
    private const string BasketAggregateFullName = "Basket.Domain.Baskets.Basket";
    private const string DbSetOpenGenericFullName = "Microsoft.EntityFrameworkCore.DbSet`1";

    private const string CatalogAclNamespace = "Basket.Infrastructure.ExternalServices.Catalog";

    /// <summary>The types in the ACL namespace that are machinery rather than wire shape.</summary>
    private static readonly string[] CatalogAclNonDtoNames =
    [
        "ProductCatalogHttpAdapter",
        "CatalogClientDependencyInjection",
    ];

    /// <summary>
    /// Catalog's wire DTOs — everything else in the ACL namespace. Derived rather than listed so a
    /// record added to the ACL is covered the moment it exists; a hardcoded list silently
    /// under-enforces the day someone forgets to extend it. Nested types are excluded because
    /// compiler-generated async state machines inside the adapter report the enclosing namespace.
    /// </summary>
    private static readonly string[] CatalogDtoFullNames = InfrastructureAssembly.GetTypes()
        .Where(type => type.Namespace == CatalogAclNamespace && !type.IsNested)
        .Where(type => !CatalogAclNonDtoNames.Contains(type.Name, StringComparer.Ordinal))
        .Select(type => type.FullName!)
        .Order(StringComparer.Ordinal)
        .ToArray();

    /// <summary>
    /// ADR-0016 + basket.md § 6: the Basket aggregate lives in Redis, not Postgres.
    /// The SQL-side <c>BasketDbContext</c> only carries outbox + inbox tables.
    /// Adding <c>DbSet&lt;Basket&gt;</c> would silently introduce a second store
    /// and violate the BC's "Redis-backed aggregate + SQL side-car" topology.
    /// </summary>
    [Fact]
    public void BasketDbContext_HasNo_DbSetOfBasket()
    {
        // Sanity-check the marker types resolve before running the rule, so a future rename
        // of the aggregate produces a clear failure rather than a silent pass.
        _ = typeof(BasketAggregate);
        _ = typeof(DbSet<>);

        var result = Types.InAssembly(InfrastructureAssembly)
            .That()
            .HaveName("BasketDbContext")
            .Should()
            .MeetCustomRule(new DoesNotContainDbSetOfBasketRule())
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "BasketDbContext must not contain a DbSet<Basket> — the Basket aggregate lives in Redis " +
            "(ADR-0016). The SQL context is outbox + inbox only");
    }

    /// <summary>
    /// ADR-0016 connection-string discipline: <c>RedisBasketRepository</c> talks to
    /// the dedicated <c>redis-basket</c> instance via <c>StackExchange.Redis</c> /
    /// FusionCache. EF Core has no business inside this repository — it would
    /// silently add a second persistence path for the aggregate.
    /// </summary>
    [Fact]
    public void RedisBasketRepository_HasNo_EfCoreDependency()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .That()
            .HaveName("RedisBasketRepository")
            .Should()
            .NotHaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Microsoft.EntityFrameworkCore.Storage",
                "Microsoft.EntityFrameworkCore.Infrastructure",
                "Npgsql.EntityFrameworkCore.PostgreSQL")
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "RedisBasketRepository must persist the aggregate exclusively via the Redis/FusionCache stack — " +
            "introducing EF Core would create a second persistence path and violate ADR-0016");
    }

    /// <summary>
    /// ACL discipline per architecture-tests.md § 2.2: Catalog HTTP DTOs are an
    /// implementation detail of the anti-corruption layer. Only
    /// <c>ProductCatalogHttpAdapter</c> may reference them; everything else
    /// (Domain, Application, the rest of Infrastructure) sees only the internal
    /// <c>ProductSnapshot</c> VO.
    /// </summary>
    [Fact]
    public void OnlyProductCatalogHttpAdapter_References_CatalogHttpDtos()
    {
        var typesReferencingCatalogDtos = Types.InAssembly(InfrastructureAssembly)
            .That()
            .HaveDependencyOnAny(CatalogDtoFullNames)
            .GetTypes()
            .Select(t => t.FullName!)
            .Where(name => !CatalogDtoFullNames.Contains(name))
            .ToList();

        // Only the adapter is allowed. The DI extension may reference adapter-as-named-client
        // by closed type, which does NOT count as a DTO reference (it binds via HttpClient name).
        var disallowedReferrers = typesReferencingCatalogDtos
            .Where(name => name != "Basket.Infrastructure.ExternalServices.Catalog.ProductCatalogHttpAdapter")
            .ToList();

        disallowedReferrers.Should().BeEmpty(
            "Only ProductCatalogHttpAdapter may reference Catalog's HTTP DTOs ({0}). All other code " +
            "must use the internal ProductSnapshot VO. Disallowed referrers: {1}",
            string.Join(", ", CatalogDtoFullNames),
            string.Join(", ", disallowedReferrers));
    }

    private sealed class DoesNotContainDbSetOfBasketRule : ICustomRule
    {
        public bool MeetsRule(TypeDefinition type)
        {
            foreach (var property in type.Properties)
            {
                if (IsDbSetOfBasket(property.PropertyType))
                {
                    return false;
                }
            }

            foreach (var field in type.Fields)
            {
                if (IsDbSetOfBasket(field.FieldType))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsDbSetOfBasket(TypeReference typeReference)
        {
            if (typeReference is not GenericInstanceType genericInstance)
            {
                return false;
            }

            // ElementType.FullName is the open-generic form (e.g. Microsoft.EntityFrameworkCore.DbSet`1)
            if (genericInstance.ElementType.FullName != DbSetOpenGenericFullName)
            {
                return false;
            }

            if (genericInstance.GenericArguments.Count != 1)
            {
                return false;
            }

            return genericInstance.GenericArguments[0].FullName == BasketAggregateFullName;
        }
    }
}
