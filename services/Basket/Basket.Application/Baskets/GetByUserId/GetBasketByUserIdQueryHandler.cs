using Basket.Application.Abstractions;
using Basket.Application.Baskets.Common.Contracts;
using Basket.Domain.Baskets.ValueObjects;
using FluentResults;
using Platform.CQRS;
using BasketAggregate = Basket.Domain.Baskets.Basket;

namespace Basket.Application.Baskets.GetByUserId;

internal sealed class GetBasketByUserIdQueryHandler : IQueryHandler<GetBasketByUserIdQuery, GetBasketResponse>
{
    private readonly IBasketRepository _repository;

    public GetBasketByUserIdQueryHandler(IBasketRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<GetBasketResponse>> HandleAsync(
        GetBasketByUserIdQuery query,
        CancellationToken ct)
    {
        var loadResult = await _repository.GetByUserIdAsync(query.UserId, ct);
        if (loadResult.IsFailed)
        {
            return loadResult.ToResult<GetBasketResponse>();
        }

        var basket = loadResult.Value;
        if (basket is null)
        {
            return Result.Ok(EmptyResponseFor(query.UserId));
        }

        return Result.Ok(MapToResponse(basket));
    }

    private static GetBasketResponse EmptyResponseFor(Guid userId)
    {
        return new GetBasketResponse
        {
            UserId = userId,
            Version = 0,
            Items = [],
            Total = null,
            CreatedAtUtc = default,
            LastModifiedAtUtc = default,
        };
    }

    private static GetBasketResponse MapToResponse(BasketAggregate basket)
    {
        var items = basket.Items.Select(MapItem).ToArray();
        var totalDto = basket.Total is { Amount: var amount }
            ? new MoneyDto(amount.Amount, amount.Currency.Name)
            : null;

        return new GetBasketResponse
        {
            UserId = basket.UserId,
            Version = basket.Version,
            Items = items,
            Total = totalDto,
            CreatedAtUtc = basket.CreatedAtUtc,
            LastModifiedAtUtc = basket.LastModifiedAtUtc,
        };
    }

    private static GetBasketItemDto MapItem(BasketItem item)
    {
        var price = item.Snapshot.Price;
        var snapshotPriceDto = new MoneyDto(price.Amount, price.Currency.Name);
        var lineTotalDto = new MoneyDto(price.Amount * item.Quantity, price.Currency.Name);

        return new GetBasketItemDto
        {
            ProductId = item.ProductId,
            Sku = item.Snapshot.Sku,
            Name = item.Snapshot.Name,
            SnapshotPrice = snapshotPriceDto,
            Quantity = item.Quantity,
            CapturedAtUtc = item.Snapshot.CapturedAtUtc,
            LineTotal = lineTotalDto,
        };
    }
}
