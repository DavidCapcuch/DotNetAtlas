using NetArchTest.Rules;

namespace Catalog.ArchitectureTests.Domain;

/// <summary>
/// Locks ADR compliance that the compiler does not enforce on its own.
/// </summary>
public class AdrComplianceTests : BaseTest
{
    /// <summary>
    /// Per ADR-0015 (time + timezone policy), <c>Catalog.Domain</c> must obtain "now" only via the
    /// injected <see cref="System.TimeProvider"/>. Static <c>DateTime.UtcNow</c> /
    /// <c>DateTimeOffset.UtcNow</c> accessors break determinism and the
    /// <c>FakeTimeProvider</c> test seam (see catalog-m4.md M4.3).
    /// </summary>
    [Fact]
    public void Domain_ShouldNot_UseStaticUtcNow()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .MeetCustomRule(new NoStaticUtcNowRule())
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Per ADR-0015, Catalog.Domain must read 'now' from TimeProvider parameters, not static " +
            "DateTime/DateTimeOffset.UtcNow getters. Thread the value through aggregate methods.");
    }
}
