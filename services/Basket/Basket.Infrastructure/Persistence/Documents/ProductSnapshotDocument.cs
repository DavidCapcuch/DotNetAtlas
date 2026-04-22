using MemoryPack;

namespace Basket.Infrastructure.Persistence.Documents;

/// <summary>
/// Persistence mirror of <c>ProductSnapshot</c>. <c>Money</c> is flattened into
/// two primitive columns (<see cref="PriceAmount"/> and
/// <see cref="PriceCurrencyName"/>) so the <c>Platform.SharedKernel</c>
/// <c>Money</c> type does not need to be annotated <c>[MemoryPackable]</c>.
/// </summary>
/// <param name="Sku">Catalog SKU at capture time.</param>
/// <param name="Name">Catalog product name at capture time.</param>
/// <param name="PriceAmount">Numeric component of the captured price.</param>
/// <param name="PriceCurrencyName">ISO 4217 three-letter currency code (matches <c>CurrencyCode.Name</c>).</param>
/// <param name="CapturedAtUtc">UTC timestamp when the snapshot was taken.</param>
[MemoryPackable]
public sealed partial record ProductSnapshotDocument(
    string Sku,
    string Name,
    decimal PriceAmount,
    string PriceCurrencyName,
    DateTimeOffset CapturedAtUtc);
