using System.ComponentModel.DataAnnotations;

namespace Invoicing.Infrastructure.Common.Config;

/// <summary>
/// Connection strings bound from the <c>ConnectionStrings</c> section. The
/// AzureStorage entry already drives <see cref="Invoicing.Infrastructure.Blobs"/>
/// and is bound via <c>BlobStorageOptions</c>; this options class adds the
/// Postgres entry that <c>PersistenceDependencyInjection</c> needs.
/// </summary>
public sealed class ConnectionStringsOptions
{
    public const string Section = "ConnectionStrings";

    [Required(ErrorMessage = $"{nameof(Invoicing)} connection string is missing", AllowEmptyStrings = false)]
    public required string Invoicing { get; set; }
}
