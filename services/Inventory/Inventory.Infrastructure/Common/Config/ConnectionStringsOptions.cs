using System.ComponentModel.DataAnnotations;

namespace Inventory.Infrastructure.Common.Config;

/// <summary>
/// Connection strings bound from the <c>ConnectionStrings</c> section.
/// Inventory owns a single Postgres database.
/// </summary>
public sealed class ConnectionStringsOptions
{
    public const string Section = "ConnectionStrings";

    [Required(ErrorMessage = $"{nameof(Inventory)} connection string is missing", AllowEmptyStrings = false)]
    public required string Inventory { get; set; }
}
