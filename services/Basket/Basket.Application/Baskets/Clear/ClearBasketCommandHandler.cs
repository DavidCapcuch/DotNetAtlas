using Basket.Application.Abstractions;
using Basket.Application.Common.Persistence;
using FluentResults;
using Platform.CQRS;
using Platform.SharedKernel.Base.DomainEvents;

namespace Basket.Application.Baskets.Clear;

internal sealed class ClearBasketCommandHandler : ICommandHandler<ClearBasketCommand>
{
    private readonly IBasketRepository _repository;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;

    public ClearBasketCommandHandler(
        IBasketRepository repository,
        IDomainEventDispatcher dispatcher,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _dispatcher = dispatcher;
        _timeProvider = timeProvider;
    }

    public async Task<Result> HandleAsync(ClearBasketCommand command, CancellationToken ct)
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

            basket.Clear(utcNow);

            var events = basket.PopDomainEvents();
            if (events.Count == 0)
            {
                // Already-empty basket.
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
