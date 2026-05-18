using Basket.Application.Abstractions;
using Basket.Application.Baskets.Common.Contracts;
using Basket.Application.Common.Data;
using Basket.Application.Common.Persistence;
using Basket.Domain.Baskets.Errors;
using FluentResults;
using Microsoft.Extensions.Logging;
using Platform.CQRS;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore.Common;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.ValueObjects;

namespace Basket.Application.Baskets.Checkout;

/// <summary>
/// Handles <see cref="CheckoutBasketCommand"/> — the Basket BC's one terminal
/// transition. Per <c>basket.md § 6.4</c>:
/// <list type="number">
///   <item>Loads the basket; 404-equivalent failure if absent or empty.</item>
///   <item>Calls <c>basket.Checkout(...)</c>, which raises <c>BasketCheckedOutDomainEvent</c>
///     carrying the snapshot plus the three pass-through courier fields.</item>
///   <item>Persists the (bumped) basket via <see cref="IBasketRepository.SaveAsync"/> under
///     optimistic concurrency. This is the C-1 fix: two parallel checkouts for the same
///     user no longer race past one another to both write the outbox row. The loser of
///     the CAS race retries once (per <c>basket.md § 5.4</c>) and, on a second loss,
///     surfaces <see cref="BasketConcurrencyError"/> (mapped to HTTP 409 at the API
///     boundary).</item>
///   <item>Dispatches the domain event. The fan-out includes
///     <c>BasketCheckoutInitiatedOutboxPublisherDomainEventHandler</c>, which writes the Avro
///     integration event to the outbox via <c>ITransactionalOutbox&lt;IBasketDbContext&gt;</c>.</item>
///   <item>Issues <see cref="Platform.ReliableMessaging.Outbox.EFCore.ITransactionalOutbox{TContext}.SaveChangesAsync"/>
///     to persist the outbox row, wrapped in
///     <see cref="DatabaseFacadeExtensions.EnsureTransactionAsync"/> so any future fan-out
///     handler that issues additional SQL writes commits (or rolls back) atomically with the
///     outbox row. Matches the convention used by the Payments / Ordering / Inventory saga
///     command handlers (<c>SagaCommandHandlerBase</c>).</item>
///   <item>After the SQL commit succeeds, deletes the Redis entry via the repository's
///     direct-<c>DEL</c> path. A delete failure is logged but NOT propagated — the outbox
///     is the source of truth, and a stale Redis entry will be cleaned up on the next
///     checkout attempt or at the 30-day TTL expiry.</item>
/// </list>
/// </summary>
internal sealed class CheckoutBasketCommandHandler : ICommandHandler<CheckoutBasketCommand, Guid>
{
    private readonly IBasketRepository _repository;
    private readonly ITransactionalOutbox<IBasketDbContext> _outbox;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CheckoutBasketCommandHandler> _logger;

    public CheckoutBasketCommandHandler(
        IBasketRepository repository,
        ITransactionalOutbox<IBasketDbContext> outbox,
        IDomainEventDispatcher dispatcher,
        TimeProvider timeProvider,
        ILogger<CheckoutBasketCommandHandler> logger)
    {
        _repository = repository;
        _outbox = outbox;
        _dispatcher = dispatcher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<Guid>> HandleAsync(CheckoutBasketCommand command, CancellationToken ct)
    {
        var shippingResult = ToAddress(command.ShippingAddress);
        var billingResult = ToAddress(command.BillingAddress);
        var addressResults = Result.Merge(shippingResult, billingResult);
        if (addressResults.IsFailed)
        {
            return Result.Fail<Guid>(addressResults.Errors);
        }

        return await BasketConcurrencyRetry.ExecuteAsync(innerCt =>
            ExecuteCheckoutAsync(command, shippingResult.Value, billingResult.Value, innerCt), ct);
    }

    private async Task<Result<Guid>> ExecuteCheckoutAsync(
        CheckoutBasketCommand command,
        Address shippingAddress,
        Address billingAddress,
        CancellationToken ct)
    {
        var loadResult = await _repository.GetByUserIdAsync(command.UserId, ct);
        if (loadResult.IsFailed)
        {
            return loadResult.ToResult<Guid>();
        }

        var basket = loadResult.Value;
        if (basket is null || basket.Items.Count == 0)
        {
            return Result.Fail<Guid>(BasketErrors.EmptyBasket());
        }

        var expectedVersion = basket.Version;
        var utcNow = _timeProvider.GetUtcNow();
        var checkoutResult = basket.Checkout(
            command.CorrelationId,
            shippingAddress,
            billingAddress,
            command.PaymentMethodId,
            utcNow);
        if (checkoutResult.IsFailed)
        {
            return checkoutResult.ToResult<Guid>();
        }

        // CAS guard: SaveAsync persists the bumped basket at the version captured BEFORE
        // Checkout(). A racer that beat us to commit causes SaveAsync to return
        // BasketConcurrencyError and we retry exactly once via BasketConcurrencyRetry —
        // preventing two parallel checkouts from each emitting an integration event.
        var saveResult = await _repository.SaveAsync(basket, expectedVersion, ct);
        if (saveResult.IsFailed)
        {
            return saveResult.ToResult<Guid>();
        }

        // Domain-event fan-out writes the outbox row via the publisher handler.
        // EnsureTransactionAsync joins an ambient transaction if one exists (e.g.
        // when checkout is invoked from inside a saga-command handler that already
        // opened one), otherwise creates a fresh one with the database's execution
        // strategy. Today the fan-out writes exactly one row so a single implicit
        // SaveChanges would have been atomic too — the wrap is preventative against a
        // future fan-out handler that issues its own SQL writes (e.g. inbox dedup or
        // audit row) and would silently break outbox-state atomicity without it.
        await _outbox.Database.EnsureTransactionAsync(
            async () =>
            {
                foreach (var domainEvent in basket.PopDomainEvents())
                {
                    await _dispatcher.DispatchAsync(domainEvent, ct);
                }

                await _outbox.SaveChangesAsync(ct);
            },
            ct);

        // After SQL commit — delete the Redis entry (bypasses FusionCache inside
        // the repository). Failure here is recoverable: the outbox is the source
        // of truth; a stale key will be cleaned on the next checkout or at TTL.
        var deleteResult = await _repository.DeleteAsync(command.UserId, ct);
        if (deleteResult.IsFailed)
        {
            _logger.LogWarning(
                "Post-checkout Redis delete failed for user {UserId} after outbox commit; relying on next-checkout cleanup or TTL.",
                command.UserId);
        }

        return Result.Ok(command.CorrelationId);
    }

    private static Result<Address> ToAddress(CheckoutAddressDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return Address.Create(
            dto.Street1,
            dto.Street2,
            dto.City,
            dto.State,
            dto.PostalCode,
            dto.CountryCode);
    }
}
