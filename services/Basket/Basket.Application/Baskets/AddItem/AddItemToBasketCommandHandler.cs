using Basket.Application.Abstractions;
using Basket.Application.Common.Persistence;
using FluentResults;
using Platform.CQRS;
using Platform.SharedKernel.Base.DomainEvents;
using BasketAggregate = Basket.Domain.Baskets.Basket;

namespace Basket.Application.Baskets.AddItem;

internal sealed class AddItemToBasketCommandHandler : ICommandHandler<AddItemToBasketCommand>
{
    private readonly IBasketRepository _repository;
    private readonly IProductCatalogQueryPort _catalog;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;

    public AddItemToBasketCommandHandler(
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

    public async Task<Result> HandleAsync(AddItemToBasketCommand command, CancellationToken ct)
    {
        // ACL call happens once — the returned snapshot is immutable and reusable
        // across both concurrency-retry attempts. Avoids double-charging Catalog.
        var snapshotResult = await _catalog.GetProductSnapshotAsync(command.ProductId, ct);
        if (snapshotResult.IsFailed)
        {
            return snapshotResult.ToResult();
        }

        var snapshot = snapshotResult.Value;

        return await BasketConcurrencyRetry.ExecuteAsync(async innerCt =>
        {
            var loadResult = await _repository.GetByUserIdAsync(command.UserId, innerCt);
            if (loadResult.IsFailed)
            {
                return loadResult.ToResult();
            }

            var utcNow = _timeProvider.GetUtcNow();
            var basket = loadResult.Value ?? BasketAggregate.Create(command.UserId, utcNow);
            var expectedVersion = basket.Version;

            var addResult = basket.AddItem(command.ProductId, snapshot, command.Quantity, utcNow);
            if (addResult.IsFailed)
            {
                return addResult;
            }

            var saveResult = await _repository.SaveAsync(basket, expectedVersion, innerCt);
            if (saveResult.IsFailed)
            {
                return saveResult;
            }

            foreach (var domainEvent in basket.PopDomainEvents())
            {
                await _dispatcher.DispatchAsync(domainEvent, innerCt);
            }

            return Result.Ok();
        }, ct);
    }
}
