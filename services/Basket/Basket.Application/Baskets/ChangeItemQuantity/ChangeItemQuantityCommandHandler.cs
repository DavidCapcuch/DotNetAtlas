using Basket.Application.Abstractions;
using Basket.Application.Common.Persistence;
using Basket.Domain.Baskets.Errors;
using FluentResults;
using Platform.CQRS;
using Platform.SharedKernel.Base.DomainEvents;

namespace Basket.Application.Baskets.ChangeItemQuantity;

internal sealed class ChangeItemQuantityCommandHandler : ICommandHandler<ChangeItemQuantityCommand>
{
    private readonly IBasketRepository _repository;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;

    public ChangeItemQuantityCommandHandler(
        IBasketRepository repository,
        IDomainEventDispatcher dispatcher,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _dispatcher = dispatcher;
        _timeProvider = timeProvider;
    }

    public async Task<Result> HandleAsync(ChangeItemQuantityCommand command, CancellationToken ct)
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
                return Result.Fail(BasketErrors.ItemNotFound(command.ProductId));
            }

            var utcNow = _timeProvider.GetUtcNow();
            var expectedVersion = basket.Version;

            var changeResult = basket.ChangeQuantity(command.ProductId, command.NewQuantity, utcNow);
            if (changeResult.IsFailed)
            {
                return changeResult;
            }

            var events = basket.PopDomainEvents();
            if (events.Count == 0)
            {
                // No-op: new quantity == existing quantity.
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
