using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Invoicing.Infrastructure.Common;

/// <summary>
/// DI extensions for the Invoicing infrastructure layer.
/// M1 stub. Subsequent milestones wire:
///   M3 — <c>IBlobStore</c> + Azurite adapter
///   M4 — <c>IPdfGenerator</c> + QuestPDF adapter
///   M5 — <c>InvoicingDbContext</c> + outbox/inbox + allocator services
///   M6 — KafkaFlow consumers + inbox dedup
/// </summary>
public static class InfrastructureDependencyInjection
{
    public static IServiceCollection AddInvoicingInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDeployedEnvironment)
    {
        // M3+: AddBlobStorage, AddPdfGeneration, AddPersistence, AddMessaging, AddHealthChecks.
        _ = configuration;
        _ = isDeployedEnvironment;
        return services;
    }
}
