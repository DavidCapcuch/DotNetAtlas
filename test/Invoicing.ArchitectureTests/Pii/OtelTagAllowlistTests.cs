using Invoicing.ArchitectureTests.Rules;
using NetArchTest.Rules;

namespace Invoicing.ArchitectureTests.Pii;

/// <summary>
/// ADR-0011 § Architecture tests: forbid <c>Activity.SetTag(\"*.address\", ...)</c>,
/// <c>SetTag(\"*.email\", ...)</c>, <c>SetTag(\"*.pan\", ...)</c> directly. PII keys
/// are also stripped at the OpenTelemetry collector ("attributes" processor in
/// <c>src/otel-collector/otelcol-config.yml</c>), but emitting them in the first
/// place is still a smell worth blocking at code-review time. This static gate
/// keeps the seam clean across all four Invoicing layer assemblies.
/// </summary>
public sealed class OtelTagAllowlistTests : BaseTest
{
    [Fact]
    public void Domain_Should_NotEmit_ForbiddenOtelTagKeys()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .MeetCustomRule(new NoForbiddenActivityTagKeysRule())
            .GetResult();
        result.FailingTypes.Should().BeEmpty(
            "Domain has no business calling Activity.SetTag — let alone with PII keys (ADR-0011)");
    }

    [Fact]
    public void Application_Should_NotEmit_ForbiddenOtelTagKeys()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .MeetCustomRule(new NoForbiddenActivityTagKeysRule())
            .GetResult();
        result.FailingTypes.Should().BeEmpty(
            "Application command/query handlers must not tag OTEL spans with PII keys (ADR-0011) — " +
            "buyer.email / buyer.address.* / *.pan / *.cvv etc. are stripped at the collector but " +
            "emitting them is still a build-break");
    }

    [Fact]
    public void Infrastructure_Should_NotEmit_ForbiddenOtelTagKeys()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .Should()
            .MeetCustomRule(new NoForbiddenActivityTagKeysRule())
            .GetResult();
        result.FailingTypes.Should().BeEmpty(
            "Kafka consumers, EF Core interceptors, and the QuestPDF generator must not tag spans " +
            "with PII keys (ADR-0011)");
    }

    [Fact]
    public void Api_Should_NotEmit_ForbiddenOtelTagKeys()
    {
        var result = Types.InAssembly(ApiAssembly)
            .Should()
            .MeetCustomRule(new NoForbiddenActivityTagKeysRule())
            .GetResult();
        result.FailingTypes.Should().BeEmpty(
            "FastEndpoints classes and ResultsExtensions must not tag spans with PII keys (ADR-0011)");
    }
}
