using System.ComponentModel.DataAnnotations;

namespace Catalog.Infrastructure.Common.Config;

/// <summary>
/// Connection strings bound from the <c>ConnectionStrings</c> section.
/// Catalog owns a single Postgres database (no Redis store).
/// </summary>
public sealed class ConnectionStringsOptions
{
    public const string Section = "ConnectionStrings";

    [Required(ErrorMessage = $"{nameof(Catalog)} connection string is missing", AllowEmptyStrings = false)]
    public required string Catalog { get; set; }
}
