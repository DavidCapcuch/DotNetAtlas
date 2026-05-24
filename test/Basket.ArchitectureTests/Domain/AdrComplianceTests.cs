using NetArchTest.Rules;

namespace Basket.ArchitectureTests.Domain;

/// <summary>
/// Locks ADR compliance that the compiler does not enforce on its own.
/// </summary>
/// <remarks>
/// Companion to <see cref="TimePolicyTests"/>: that file enforces a stricter
/// Basket-specific superset (bare <c>System.DateTime</c> is forbidden BC-wide), while
/// this file enforces the canonical Catalog/Payments rule for cross-BC symmetry —
/// no static <c>UtcNow</c>/<c>Now</c>/<c>Today</c> getters, walking async state
/// machines via <see cref="BaseTest.NoStaticUtcNowRule"/>. Both rules pass today;
/// the BC-specific one is retained as the source of truth for Basket policy.
/// </remarks>
public class AdrComplianceTests : BaseTest
{
    /// <summary>
    /// Per ADR-0015 (time + timezone policy), <c>Basket.Domain</c> must obtain "now" only via the
    /// injected <see cref="System.TimeProvider"/> or the <c>utcNow</c> argument threaded into
    /// aggregate command methods. Static <c>DateTime.UtcNow</c> / <c>DateTimeOffset.UtcNow</c>
    /// accessors break determinism and the <c>FakeTimeProvider</c> test seam.
    /// </summary>
    [Fact]
    public void Domain_ShouldNot_UseStaticUtcNow()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .MeetCustomRule(new NoStaticUtcNowRule())
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Per ADR-0015, Basket.Domain must read 'now' from TimeProvider parameters or " +
            "explicit utcNow arguments, not static DateTime/DateTimeOffset.UtcNow getters. " +
            "Thread the value through aggregate methods (Basket.Create(userId, utcNow) etc.).");
    }
}
