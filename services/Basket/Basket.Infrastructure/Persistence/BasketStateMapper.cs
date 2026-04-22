using Basket.Domain.Baskets.ValueObjects;
using Basket.Infrastructure.Persistence.Documents;
using Platform.SharedKernel.ValueObjects;
using BasketAggregate = Basket.Domain.Baskets.Basket;

namespace Basket.Infrastructure.Persistence;

/// <summary>
/// Maps between the <see cref="BasketAggregate"/> domain root and its
/// <see cref="BasketStateDocument"/> persistence mirror. Lives at the
/// Infrastructure seam so the domain stays free of <c>[MemoryPackable]</c>
/// and <c>Money</c> (which cannot be annotated because it sits in
/// <c>Platform.SharedKernel</c>) crosses the boundary as two primitive fields.
/// </summary>
internal static class BasketStateMapper
{
    public static BasketStateDocument ToDocument(BasketAggregate basket)
    {
        ArgumentNullException.ThrowIfNull(basket);

        var itemDocuments = new List<BasketItemDocument>(basket.Items.Count);
        foreach (var item in basket.Items)
        {
            itemDocuments.Add(new BasketItemDocument(
                item.ProductId,
                new ProductSnapshotDocument(
                    item.Snapshot.Sku,
                    item.Snapshot.Name,
                    item.Snapshot.Price.Amount,
                    item.Snapshot.Price.Currency.Name,
                    item.Snapshot.CapturedAtUtc),
                item.Quantity));
        }

        var payload = new BasketDocument(
            basket.UserId,
            itemDocuments,
            basket.CreatedAtUtc,
            basket.LastModifiedAtUtc);

        return new BasketStateDocument(basket.Version, payload);
    }

    public static BasketAggregate ToDomain(BasketStateDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(document.Payload);

        var payload = document.Payload;
        var items = new List<BasketItem>(payload.Items.Count);
        foreach (var itemDocument in payload.Items)
        {
            var currency = CurrencyCode.FromName(itemDocument.Snapshot.PriceCurrencyName, ignoreCase: false);
            var price = new Money(itemDocument.Snapshot.PriceAmount, currency);
            var snapshot = new ProductSnapshot(
                itemDocument.Snapshot.Sku,
                itemDocument.Snapshot.Name,
                price,
                itemDocument.Snapshot.CapturedAtUtc);

            items.Add(new BasketItem(itemDocument.ProductId, snapshot, itemDocument.Quantity));
        }

        return BasketAggregate.Rehydrate(
            payload.UserId,
            document.Version,
            payload.CreatedAtUtc,
            payload.LastModifiedAtUtc,
            items);
    }
}
