using System.ComponentModel.DataAnnotations;

namespace Invoicing.Application.Blobs;

/// <summary>
/// Configuration for the Invoicing blob-storage adapter. Binds <c>BlobStorage:*</c> for
/// container + CDN settings; the connection string itself lives under
/// <c>ConnectionStrings:AzureStorage</c> (repo convention; see sibling services'
/// <c>appsettings.json</c>) and is injected by DI, not bound from this section.
/// Lives in the Application layer (M7 move) so command handlers can read
/// <see cref="InvoicesContainerName"/> alongside the <see cref="IBlobStore"/> dependency
/// without crossing into Infrastructure.
/// </summary>
public sealed class BlobStorageOptions
{
    public const string SectionName = "BlobStorage";

    /// <summary>Azure Storage connection string \u2014 Azurite dev key or managed-identity URI in prod. Populated by DI from <c>ConnectionStrings:AzureStorage</c>.</summary>
    [Required]
    [MinLength(1)]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Container name for invoice PDFs (default <c>invoices</c> per ADR-0017).</summary>
    [Required]
    [MinLength(1)]
    public string InvoicesContainerName { get; set; } = "invoices";

    /// <summary>
    /// Optional public base URI for SAS URLs \u2014 e.g., the nginx-cdn front
    /// (<c>http://localhost:8080</c>) in local dev, or the Azure Front Door endpoint
    /// in production. When null, SAS URLs point directly at the blob service.
    /// Must be scheme+authority only; any path component is ignored during CDN rewrite.
    /// </summary>
    public Uri? PublicBaseUri { get; set; }
}
