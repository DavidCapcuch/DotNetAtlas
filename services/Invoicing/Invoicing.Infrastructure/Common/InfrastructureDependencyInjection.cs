using Azure.Storage.Blobs;
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
/// <c>InvoicingDbContext</c> + the gap-free number allocators (ADR-0018).
/// Subsequent milestones wire:
///   M6 — projection consumers + inbox dedup + outbox plumbing
///   M7 — IssueInvoice / IssueCreditNote command handlers + outbox publishers
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

        services.AddBlobStorage(configuration);
        services.AddPdfGeneration(configuration);
        services.AddPersistence(configuration, isDeployedEnvironment);

        // M6+: AddMessaging (KafkaFlow consumers + inbox dedup), AddHealthChecks.
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
