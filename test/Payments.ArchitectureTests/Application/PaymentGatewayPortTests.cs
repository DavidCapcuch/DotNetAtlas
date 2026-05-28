using NetArchTest.Rules;
using Payments.Application.Abstractions;

namespace Payments.ArchitectureTests.Application;

/// <summary>
/// Locks the Payments gateway port-and-adapter shape per <c>payments.md</c>
/// &lt;session_management&gt;: the <see cref="IPaymentGateway"/> port lives in
/// <c>Payments.Application.Abstractions</c>; the Application layer must never reference any
/// concrete adapter (<c>StubPaymentGateway</c> today; a real gateway adapter in production).
/// The layer test in <c>CleanArchitectureLayerTests</c> already forbids
/// <c>Payments.Infrastructure</c> from <c>Payments.Application</c> transitively; this file makes
/// the Payments-specific intent explicit and gives the build an early/named failure if a
/// contributor ever adds <c>using Payments.Infrastructure.ExternalServices.PaymentGateway;</c>
/// to an Application file.
/// </summary>
public sealed class PaymentGatewayPortTests : BaseTest
{
    [Fact]
    public void IPaymentGateway_Should_LiveIn_ApplicationAbstractions()
    {
        typeof(IPaymentGateway).Namespace.Should().Be(
            "Payments.Application.Abstractions",
            "Per payments.md, the gateway port is owned by the Application layer (Hexagonal: " +
            "Application defines the contract; Infrastructure provides the adapter).");

        typeof(IPaymentGateway).Assembly.GetName().Name.Should().Be(
            ApplicationAssembly.GetName().Name,
            "IPaymentGateway must ship in Payments.Application — Infrastructure depends on " +
            "Application, not the other way round.");
    }

    [Fact]
    public void Application_ShouldNot_Reference_ConcretePaymentGatewayAdapters()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Payments.Infrastructure",
                "Payments.Infrastructure.ExternalServices.PaymentGateway")
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Application code must depend on the IPaymentGateway port only — never on " +
            "StubPaymentGateway or any other concrete gateway adapter under " +
            "Payments.Infrastructure.ExternalServices.PaymentGateway. The DI container wires the " +
            "concrete adapter at composition root.");
    }
}
