using Microsoft.Extensions.DependencyInjection;
using Payments.Application.Abstractions;

namespace Payments.Infrastructure.ExternalServices.PaymentGateway;

/// <summary>
/// DI wiring for the payment-gateway adapter. v1 binds <see cref="IPaymentGateway"/> to
/// <see cref="StubPaymentGateway"/>; production deployments replace the registration with a
/// real adapter (Stripe, Adyen, Braintree).
/// </summary>
/// <remarks>
/// Internal helper. Intentionally not yet wired from a host — the future
/// <c>Payments.Infrastructure/Common/InfrastructureDependencyInjection.cs</c> (M5) and
/// <c>Payments.Api/Program.cs</c> (M6) will compose this extension into the application's
/// DI graph alongside DbContext, Kafka consumers, and outbox-relay configuration.
/// </remarks>
internal static class PaymentGatewayDependencyInjection
{
    /// <summary>
    /// Registers the v1 stub <see cref="IPaymentGateway"/>. Singleton lifetime: the stub is
    /// stateless (no instance fields, no captured resources). The production adapter that
    /// will replace this in M5+ — wrapping <see cref="System.Net.Http.HttpClient"/> or a
    /// gateway SDK — is also intended to be a singleton, so the lifetime contract is
    /// forward-compatible.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddPaymentGateway(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IPaymentGateway, StubPaymentGateway>();

        return services;
    }
}
