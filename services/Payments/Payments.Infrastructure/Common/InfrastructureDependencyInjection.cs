using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Payments.Infrastructure.ExternalServices.PaymentGateway;

namespace Payments.Infrastructure.Common;

/// <summary>
/// Composition root for the Payments Infrastructure layer. Called from
/// <c>Payments.Api.Program.cs</c> after <c>AddServiceDefaults</c> and
/// <c>AddApplication</c>. Order: observability → persistence → kafka messaging →
/// health checks → gateway adapter.
/// </summary>
public static class InfrastructureDependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        ConfigurationManager configuration,
        bool isDeployedEnvironment)
    {
        services
            .AddOpenTelemetry(isDeployedEnvironment, configuration)
            .AddDatabase(configuration, isDeployedEnvironment)
            .AddKafkaMessaging(configuration)
            .AddPaymentsHealthChecks(configuration)
            .AddPaymentGateway();

        return services;
    }
}
