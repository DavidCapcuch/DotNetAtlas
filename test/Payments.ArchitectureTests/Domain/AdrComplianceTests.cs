using NetArchTest.Rules;

namespace Payments.ArchitectureTests.Domain;

/// <summary>
/// Locks ADR compliance that the compiler does not enforce on its own.
/// Covers the two M7-explicit Payments deliverables: ADR-0015 no-static-UtcNow and ADR-0011
/// no-cardholder-data field names.
/// </summary>
public class AdrComplianceTests : BaseTest
{
    /// <summary>
    /// Cardholder-data field names forbidden by ADR-0011 / payments.md &lt;applicable_adrs&gt;.
    /// PAN / CVV must never enter Payments — the gateway tokenises into <c>PaymentMethodId</c>.
    /// Match is case-insensitive and exact (whole field/property name) so harmless tokens like
    /// <c>panel</c> or <c>cvvalue</c> are not false-positives.
    /// </summary>
    private static readonly string[] CardholderDataFieldNames =
    [
        "pan",
        "cvv",
        "cardNumber",
        "cardholderName",
        "cardholder",
    ];

    /// <summary>
    /// Per ADR-0015 (time + timezone policy), <c>Payments.Domain</c> must obtain "now" only via
    /// the injected <see cref="System.TimeProvider"/>. Static <c>DateTime.UtcNow</c> /
    /// <c>DateTimeOffset.UtcNow</c> accessors break determinism and the
    /// <c>FakeTimeProvider</c> test seam.
    /// </summary>
    [Fact]
    public void Domain_ShouldNot_UseStaticUtcNow()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .MeetCustomRule(new NoStaticUtcNowRule())
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Per ADR-0015, Payments.Domain must read 'now' from TimeProvider parameters (utcNow), not " +
            "static DateTime/DateTimeOffset.UtcNow getters. Thread the value through aggregate methods.");
    }

    /// <summary>
    /// Per ADR-0011 + payments.md &lt;applicable_adrs&gt;, no field or property in
    /// <c>Payments.Domain</c> may carry a cardholder-data-shaped name (PAN / CVV /
    /// cardNumber / cardholderName / cardholder). The aggregate uses tokenised
    /// <c>PaymentMethodId</c>; raw card data must never be reachable from domain code.
    /// </summary>
    [Fact]
    public void Domain_ShouldNot_DefineCardholderDataFields()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .MeetCustomRule(new NoForbiddenFieldNamesRule(CardholderDataFieldNames))
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Per ADR-0011, Payments.Domain must not define fields/properties named " +
            "pan / cvv / cardNumber / cardholderName / cardholder (case-insensitive). " +
            "Raw cardholder data never enters Payments — the gateway tokenises into PaymentMethodId.");
    }

    /// <summary>
    /// Per ADR-0011, the Infrastructure layer must not introduce cardholder-data-shaped fields
    /// either — no Postgres column, EF mapping, Kafka DTO, or gateway-adapter property may name
    /// itself <c>pan</c> / <c>cvv</c> / <c>cardNumber</c> / <c>cardholderName</c> /
    /// <c>cardholder</c>. The teaching artifact is intentional: Payments handles tokens only.
    /// </summary>
    [Fact]
    public void Infrastructure_ShouldNot_DefineCardholderDataFields()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .Should()
            .MeetCustomRule(new NoForbiddenFieldNamesRule(CardholderDataFieldNames))
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Per ADR-0011, Payments.Infrastructure must not define fields/properties named " +
            "pan / cvv / cardNumber / cardholderName / cardholder (case-insensitive). " +
            "Use tokenised PaymentMethodId / GatewayTransactionId only.");
    }
}
