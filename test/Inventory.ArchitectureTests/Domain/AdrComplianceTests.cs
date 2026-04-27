using NetArchTest.Rules;

namespace Inventory.ArchitectureTests.Domain;

/// <summary>
/// Locks ADR compliance that the compiler does not enforce on its own.
/// </summary>
public class AdrComplianceTests : BaseTest
{
    /// <summary>
    /// Per ADR-0015 (time + timezone policy), <c>Inventory.Domain</c> must obtain "now" only via
    /// command-method parameters carrying a <see cref="System.DateTimeOffset"/> sourced from the
    /// caller's <see cref="System.TimeProvider"/>. Static <c>DateTime.UtcNow</c> /
    /// <c>DateTimeOffset.UtcNow</c> accessors break determinism and the
    /// <c>FakeTimeProvider</c> test seam used by the <c>ReservationExpiryWorker</c> +
    /// example-mapping race tests.
    /// </summary>
    [Fact]
    public void Domain_ShouldNot_UseStaticUtcNow()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .MeetCustomRule(new NoStaticUtcNowRule())
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Per ADR-0015, Inventory.Domain must read 'now' from caller-supplied DateTimeOffset " +
            "parameters, not static DateTime/DateTimeOffset.UtcNow getters. Thread the value " +
            "through aggregate command methods (StockItem.Reserve(..., occurredOnUtc) etc.).");
    }
}
