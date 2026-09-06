using System.ComponentModel.DataAnnotations;

namespace EShop.BFF.Infrastructure.Messaging.Config;

/// <summary>
/// The published-language topics the <c>bff-group</c> invalidator subscribes to (bff.md § 2.2). The Catalog
/// + Inventory topics feed the <c>home-page</c> tag; <c>basket.sessions</c> feeds the per-buyer
/// <c>basket-bff-{UserId}</c> tag. The BFF never produces to them. Bound from section <c>Topics</c>.
/// </summary>
public sealed class BffTopicsOptions
{
    public const string Section = "Topics";

    private const int MaximumKafkaTopicLength = 249;

    /// <summary>Catalog product lifecycle events (<c>ProductCreated/PriceChanged/Discontinued</c>).</summary>
    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string CatalogProducts { get; set; }

    /// <summary>Catalog category lifecycle events (<c>CategoryCreated</c>).</summary>
    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string CatalogCategories { get; set; }

    /// <summary>Inventory stock-availability threshold crossings (<c>StockLevelChanged</c>).</summary>
    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string InventoryStockEvents { get; set; }

    /// <summary>Basket session lifecycle events (<c>BasketCheckoutInitiated</c>) — clears the buyer's basket cache.</summary>
    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string BasketSessions { get; set; }

    /// <summary>
    /// The four topics the cache-invalidation consumer subscribes to.
    /// </summary>
    /// <remarks>
    /// No dead-letter siblings: the BFF's consumer pipeline registers no <c>AddDeadLetter</c>
    /// middleware and this type carries no suffix, so no <c>*.Bff.DLT</c> topic exists or should.
    /// Verified against the <c>kafka-create-topic</c> block in <c>docker-compose.yaml</c>.
    /// </remarks>
    public string[] GetAllTopics() =>
    [
        CatalogProducts,
        CatalogCategories,
        InventoryStockEvents,
        BasketSessions
    ];
}
