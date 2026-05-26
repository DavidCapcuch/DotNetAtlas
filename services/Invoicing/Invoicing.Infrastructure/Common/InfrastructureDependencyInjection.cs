using Azure.Storage.Blobs;
using Invoicing.Application.Blobs;
using Invoicing.Application.Pdf;
using Invoicing.Infrastructure.Blobs;
using Invoicing.Infrastructure.Pdf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Invoicing.Infrastructure.Common;

/// <summary>
/// DI extensions for the Invoicing infrastructure layer.
/// M1 stub → M3 adds <c>IBlobStore</c> + Azurite adapter → M4 adds
/// <c>IPdfGenerator</c> + QuestPDF adapter → M5 adds
/// <c>InvoicingDbContext</c> + the gap-free number allocators (ADR-0018) →
/// M6 adds the four enrichment-projection KafkaFlow consumers + inbox dedup.
/// Subsequent milestones wire:
///   M7 — IssueInvoice / IssueCreditNote command handlers + outbox publishers.
/// </summary>
public static class InfrastructureDependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        ConfigurationManager configuration,
        bool isDeployedEnvironment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // EF Core sensitive-data logging exposes parameter values (PII-bearing _enc
        // columns per ADR-0011). Platform convention (#121): allow it everywhere
        // EXCEPT deployed environments (Staging/Production) — Development, Test,
        // and the Testing test-host need the visibility for debugging.
        var enableSensitiveDataLogging = !isDeployedEnvironment;

        services.AddOpenTelemetry(isDeployedEnvironment, configuration);
        services.AddBlobStorage(configuration);
        services.AddPdfGeneration(configuration);
        services.AddDatabase(configuration, enableSensitiveDataLogging, isDeployedEnvironment);
        services.AddKafkaMessaging(configuration);
        services.AddInvoicingHealthChecks(configuration);

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

    internal static IServiceCollection AddPdfGeneration(this IServiceCollection services, IConfiguration configuration)
    {
        // Seller-side display strings (legal entity name + legal footer) live under the
        // PdfGeneration section. The adapter itself (QuestPdfInvoiceGenerator) holds no
        // mutable state and is registered as a singleton, matching AzureBlobStore above.
        services
            .AddOptions<PdfGenerationOptions>()
            .Bind(configuration.GetSection(PdfGenerationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IPdfGenerator, QuestPdfInvoiceGenerator>();

        return services;
    }
}
