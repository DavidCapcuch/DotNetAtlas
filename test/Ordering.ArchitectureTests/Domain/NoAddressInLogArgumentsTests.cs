using NetArchTest.Rules;
using Ordering.ArchitectureTests.Rules;

namespace Ordering.ArchitectureTests.Domain;

/// <summary>
/// Regression guard for ADR-0011 §"No PII in log lines". Walks every method
/// in every Ordering layer assembly that depends on
/// <c>Microsoft.Extensions.Logging</c> (Application, Infrastructure, API)
/// and refuses any method body that BOTH calls a <c>Log*</c> helper AND
/// touches <c>Platform.SharedKernel.ValueObjects.Address</c>. The
/// production-code grep is clean today; this rule keeps it that way.
/// </summary>
public sealed class NoAddressInLogArgumentsTests : BaseTest
{
    [Fact]
    public void ApplicationAssembly_DoesNotLogAddressTypedArguments()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .MeetCustomRule(new DoesNotLogPiiAddressRule())
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Per ADR-0011 the Address VO must never be passed to ILogger.Log* — " +
            "shipping/billing address values are PII and must stay out of log lines.");
    }

    [Fact]
    public void InfrastructureAssembly_DoesNotLogAddressTypedArguments()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .Should()
            .MeetCustomRule(new DoesNotLogPiiAddressRule())
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Per ADR-0011 the Address VO must never be passed to ILogger.Log* — " +
            "shipping/billing address values are PII and must stay out of log lines.");
    }

    [Fact]
    public void ApiAssembly_DoesNotLogAddressTypedArguments()
    {
        var result = Types.InAssembly(ApiAssembly)
            .Should()
            .MeetCustomRule(new DoesNotLogPiiAddressRule())
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Per ADR-0011 the Address VO must never be passed to ILogger.Log* — " +
            "shipping/billing address values are PII and must stay out of log lines.");
    }
}
