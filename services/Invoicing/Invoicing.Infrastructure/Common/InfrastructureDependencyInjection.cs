using Azure.Storage.Blobs;
using Invoicing.Infrastructure.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Invoicing.Infrastructure.Common;

/// <summary>
/// DI extensions for the Invoicing infrastructure layer.
/// M1 stub \u2192 M3 adds <c>IBlobStore</c> + Azurite adapter. Subsequent milestones wire:
///   M4 \u2014 <c>IPdfGenerator</c> + QuestPDF adapter
///   M5 \u2014 <c>InvoicingDbContext</c> + outbox/inbox + allocator services
///   M6 \u2014 KafkaFlow consumers + inbox dedup
/// </summary>
public static class InfrastructureDependencyInjection
{
    public static IServiceCollection AddInvoicingInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDeployedEnvironment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        _ = isDeployedEnvironment; // reserved for env-specific wiring in later milestones.

        services.AddBlobStorage(configuration);

        // M4+: AddPdfGeneration, AddPersistence, AddMessaging, AddHealthChecks.
        return services;
    }

    internal static IServiceCollection AddBlobStorage(this IServiceCollection services, IConfiguration configuration)
    {
        // Connection string lives under ConnectionStrings:AzureStorage per repo convention
        // (ADR-0017 \u00a7 Implementation Notes + every sibling BC's appsettings.json). The
        // BlobStorage section holds only container name + optional CDN base URI.
        services
            .AddOptions<BlobStorageOptions>()
            .Configure((BlobStorageOptions opts) =>
            {
                opts.ConnectionString = configuration.GetConnectionString("AzureStorage") ?? string.Empty;
                configuration.GetSection(BlobStorageOptions.SectionName).Bind(opts);
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<BlobStorageOptions>>().Value;
            return new BlobServiceClient(opts.ConnectionString);
        });

        services.AddSingleton<IBlobStore, AzureBlobStore>();

        return services;
    }
}
