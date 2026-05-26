using System.ComponentModel.DataAnnotations;

namespace Basket.Infrastructure.Common.Config;

/// <summary>
/// Connection strings bound from the <c>ConnectionStrings</c> section. Basket
/// owns a Postgres SQL side-car (ADR-0003 — outbox + inbox only) and a
/// dedicated Redis instance for the aggregate primary store (ADR-0016 — the
/// <c>Redis:Basket</c> connection is consumed by
/// <see cref="Basket.Infrastructure.Persistence.PersistenceDependencyInjection"/>
/// and the health check). The Redis connection string is read as a literal
/// key because its config path contains a colon (hierarchical separator) and
/// cannot be bound to a CLR property name.
/// </summary>
public sealed class ConnectionStringsOptions
{
    public const string Section = "ConnectionStrings";

    [Required(ErrorMessage = $"{nameof(Basket)} connection string is missing", AllowEmptyStrings = false)]
    public required string Basket { get; set; }
}
