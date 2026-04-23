using Basket.Application.Abstractions;
using Basket.Application.Common.Persistence;
using FluentResults;
using Platform.CQRS;
using Platform.SharedKernel.Base.DomainEvents;

namespace Basket.Application.Baskets.RefreshPrices;

internal sealed class RefreshBasketPricesCommandHandler : ICommandHandler<RefreshBasketPricesCommand>
{
    private readonly IBasketRepository _repository;
    private readonly IProductCatalogQueryPort _catalog;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;

    public RefreshBasketPricesCommandHandler(
        IBasketRepository repository,
        IProductCatalogQueryPort catalog,
        IDomainEventDispatcher dispatcher,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _catalog = catalog;
        _dispatcher = dispatcher;
        _timeProvider = timeProvider;
    }

    public async Task<Result> HandleAsync(RefreshBasketPricesCommand command, CancellationToken ct)
    {
        return await BasketConcurrencyRetry.ExecuteAsync(async innerCt =>
        {
            var loadResult = await _repository.GetByUserIdAsync(command.UserId, innerCt);
            if (loadResult.IsFailed)
            {
                return loadResult.ToResult();
            }

            var basket = loadResult.Value;
            if (basket is null || basket.Items.Count == 0)
            {
                return Result.Ok();
            }

            // We intentionally re-query Catalog inside the retry scope — unlike
            // AddItemToBasketCommandHandler where the product is a command input,
            // here the set of items may have changed between attempts (a concurrent
            // writer may have added or removed lines). Re-fetching guarantees the
            // refresh applies to the current set.
            var distinctIds = basket.Items.Select(i => i.ProductId).Distinct().ToArray();
            var manyResult = await _catalog.GetManyAsync(distinctIds, innerCt);
            if (manyResult.IsFailed)
            {
                return manyResult.ToResult();
            }

            var utcNow = _timeProvider.GetUtcNow();
            var expectedVersion = basket.Version;

            var refreshResult = basket.RefreshPrices(manyResult.Value, utcNow);
            if (refreshResult.IsFailed)
            {
                return refreshResult;
            }

            var events = basket.PopDomainEvents();
            if (events.Count == 0)
            {
                // Aggregate short-circuited: no prices actually changed.
                return Result.Ok();
            }

            var saveResult = await _repository.SaveAsync(basket, expectedVersion, innerCt);
            if (saveResult.IsFailed)
            {
                return saveResult;
            }

            foreach (var domainEvent in events)
            {
                await _dispatcher.DispatchAsync(domainEvent, innerCt);
            }

            return Result.Ok();
        }, ct);
    }
}
