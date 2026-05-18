namespace Basket.Infrastructure.Common.Config;

/// <summary>
/// Centralized names for the Basket service's connection-string entries in
/// <c>ConnectionStrings</c> (appsettings). Kept as constants so references are
/// compiler-checked and grep-able; ADR-0016 mandates the
/// <c>redis-basket</c> / <c>redis-cache</c> split and these names enforce it at
/// the API surface.
/// </summary>
internal static class ConnectionStringNames
{
    /// <summary>
    /// Primary basket store — <c>redis-basket</c> (AOF, noeviction). The
    /// <c>IConnectionMultiplexer</c> registered under this key is the ONLY one
    /// the basket repository may use (ADR-0016).
    /// </summary>
    public const string BasketRedis = "Redis:Basket";

    /// <summary>
    /// Postgres SQL side-car — carries outbox + inbox tables only (ADR-0003 +
    /// basket.md § 5.5). The <c>BasketDbContext</c> binds to this key in
    /// <c>Common.PersistenceDependencyInjection.AddDatabase</c>.
    /// </summary>
    public const string Basket = "Basket";
}
