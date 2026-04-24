using System.ComponentModel.DataAnnotations;

namespace Ordering.Infrastructure.Common.Config;

/// <summary>
/// Connection strings bound from <c>ConnectionStrings</c> section.
/// Ordering owns a single Postgres database (no Redis in v1).
/// </summary>
public sealed class ConnectionStringsOptions
{
    public const string Section = "ConnectionStrings";

    [Required(ErrorMessage = $"{nameof(Ordering)} connection string is missing", AllowEmptyStrings = false)]
    public required string Ordering { get; set; }
}
