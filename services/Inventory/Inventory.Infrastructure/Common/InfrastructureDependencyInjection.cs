using Inventory.Infrastructure.BackgroundJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Infrastructure.Common;

/// <summary>
/// Composition root for the Inventory Infrastructure layer. Called from
/// <c>Inventory.API.Program.cs</c> after <c>AddApplication</c>. Wires the
/// persistence slice (DbContext, EF Core, event-store repository), the
/// messaging slice (KafkaFlow cluster + 3 consumers + transactional outbox
/// + inbox dedup), and the M7 health-check surface (Self / DB / Kafka per
/// <c>eshop-master-design.md § 11</c>).
/// </summary>
/// <remarks>
/// The M6 <see cref="ReservationExpiryWorker"/> hosted service is NOT
/// registered here — Program.cs guards its registration behind
/// <c>!IsTesting()</c> via <see cref="AddReservationExpiryWorker"/>, mirroring
/// the Kafka cluster boot guard. Functional tests stand up the host but skip
/// the worker; M6 integration tests resolve <c>ReservationExpiryWorker</c>
/// directly from DI without the hosted-service loop.
/// </remarks>
public static class InfrastructureDependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        ConfigurationManager configuration,
        bool isDeployedEnvironment)
    {
        services
            .AddDatabase(configuration, isDeployedEnvironment)
            .AddMessaging(configuration)
            .AddInventoryHealthChecks(configuration);

        return services;
    }

    /// <summary>
    /// Registers the M6 <see cref="ReservationExpiryWorker"/> as a hosted
    /// service. Program.cs guards this out of the Testing environment so the
    /// functional-test fixture's eager host start doesn't fire the worker
    /// before EF migrations run. Production / dev / staging always register.
    /// </summary>
    public static IServiceCollection AddReservationExpiryWorker(this IServiceCollection services)
    {
        services.AddHostedService<ReservationExpiryWorker>();
        return services;
    }
}
