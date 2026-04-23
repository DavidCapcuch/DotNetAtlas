using Basket.Application.Abstractions;
using Basket.Application.Common.Persistence;
using FluentResults;
using Platform.CQRS;
using Platform.SharedKernel.Base.DomainEvents;

namespace Basket.Application.Baskets.RemoveItem;

internal sealed class RemoveItemFromBasketCommandHandler : ICommandHandler<RemoveItemFromBasketCommand>
{
    private readonly IBasketRepository _repository;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;

    public RemoveItemFromBasketCommandHandler(
        IBasketRepository repository,
        IDomainEventDispatcher dispatcher,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _dispatcher = dispatcher;
        _timeProvider = timeProvider;
    }

    public async Task<Result> HandleAsync(RemoveItemFromBasketCommand command, CancellationToken ct)
    {
        return await BasketConcurrencyRetry.ExecuteAsync(async innerCt =>
        {
            var loadResult = await _repository.GetByUserIdAsync(command.UserId, innerCt);
            if (loadResult.IsFailed)
            {
                return loadResult.ToResult();
            }

            var basket = loadResult.Value;
            if (basket is null)
            {
                return Result.Ok();
            }

            var utcNow = _timeProvider.GetUtcNow();
            var expectedVersion = basket.Version;

            var removeResult = basket.RemoveItem(command.ProductId, utcNow);
            if (removeResult.IsFailed)
            {
                return removeResult;
            }

            var events = basket.PopDomainEvents();
            if (events.Count == 0)
            {
                // Idempotent no-op: item was not present, aggregate did not mutate.
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
