using NetArchTest.Rules;

namespace Invoicing.ArchitectureTests.Domain;

public sealed class NoStaticUtcNowInDomainTests : BaseTest
{
    [Fact]
    public void DomainAssembly_ShouldNotCall_StaticUtcNow()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .MeetCustomRule(new NoStaticUtcNowRule())
            .GetResult();
        result.FailingTypes.Should().BeEmpty(
            "Per ADR-0015, domain code receives DateTimeOffset via parameters or " +
            "TimeProvider — never reads the static UtcNow/Now properties");
    }
}
