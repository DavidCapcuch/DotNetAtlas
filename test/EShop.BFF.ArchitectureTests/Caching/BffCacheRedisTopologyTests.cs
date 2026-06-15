using NetArchTest.Rules;

namespace EShop.BFF.ArchitectureTests.Caching;

/// <summary>
/// ADR-0016 connection-string discipline (the AC-required arch test for #327): the BFF's FusionCache
/// distributed cache + backplane bind <c>Redis:Cache</c> (the volatile redis-cache instance) and MUST
/// NOT touch <c>Redis:Basket</c> (the authoritative basket store). A cross-use would couple the
/// volatile edge cache to the durable basket instance — a shared-failure-domain bug.
/// </summary>
public sealed class BffCacheRedisTopologyTests : BaseTest
{
    [Fact]
    public void Infrastructure_DoesNotReference_RedisBasket()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .Should()
            .MeetCustomRule(new DoesNotLoadStringContainingRule("Redis:Basket"))
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "no BFF Infrastructure type may reference the 'Redis:Basket' connection string (ADR-0016): {0}",
            string.Join(", ", result.FailingTypes?.Select(t => t.Name) ?? []));
    }

    [Fact]
    public void CacheWiring_BindsRedisCache()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .That()
            .HaveName("BffCacheDependencyInjection")
            .Should()
            .MeetCustomRule(new LoadsStringRule("Redis:Cache"))
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "BffCacheDependencyInjection must bind the 'Redis:Cache' connection string (ADR-0016)");
    }
}
