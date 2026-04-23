using Ardalis.Specification.EntityFrameworkCore;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ordering.Application.Common.Data;
using Ordering.Domain.Baskets;
using Ordering.Domain.Orders;
using Ordering.Domain.Orders.Specifications;
using Platform.CQRS;
using Platform.SharedKernel.Exceptions;
using Platform.SharedKernel.ValueObjects;

namespace Ordering.Application.Orders.CreateOrder;

/// <summary>
/// Handles <see cref="CreateOrderCommand"/>: translates the saga DTO to the
/// domain <c>BasketSnapshot</c> ACL, calls <c>Order.CreateFromBasket</c>, and
/// persists. Idempotent on <see cref="CreateOrderCommand.CorrelationId"/> so
/// Kafka redelivery or saga retries cannot create duplicate orders.
/// </summary>
/// <remarks>
/// Factory-side invariants (I-6..I-9) surface as <see cref="DataIntegrityException"/>
/// from <c>Order.CreateFromBasket</c> — they are bug-class (the validator
/// should have caught every known user-error shape before the handler).
/// These exceptions bubble up and are routed to DLT by M4's Kafka middleware.
/// </remarks>
public sealed class CreateOrderCommandHandler : ICommandHandler<CreateOrderCommand, Guid>
{
    private readonly IOrderingDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CreateOrderCommandHandler> _logger;

    public CreateOrderCommandHandler(
        IOrderingDbContext dbContext,
        TimeProvider timeProvider,
        ILogger<CreateOrderCommandHandler> logger)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<Guid>> HandleAsync(CreateOrderCommand command, CancellationToken ct)
    {
        // Idempotency: if an order already exists for this CorrelationId, return its id.
        var existing = await _dbContext.Orders
            .WithSpecification(new OrderByCorrelationIdSpec(command.CorrelationId))
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            _logger.LogInformation(
                "CreateOrderCommand replayed for CorrelationId {CorrelationId} — returning existing OrderId {OrderId}",
                command.CorrelationId, existing.Id);
            return Result.Ok(existing.Id);
        }

        // Translate to domain types. DataIntegrityException on any shared-kernel
        // VO failure — all user-shape issues were caught by the validator before us.
        var currencyResult = CreateCurrency(command.Currency);
        var shippingAddressResult = ToAddress(command.ShippingAddress, nameof(command.ShippingAddress));
        var billingAddressResult = ToAddress(command.BillingAddress, nameof(command.BillingAddress));

        var basket = new BasketSnapshot(
            command.BuyerId,
            currencyResult,
            [.. command.Items.Select(i => new BasketSnapshotItem(
                i.ProductId,
                i.Sku,
                i.Name,
                i.Quantity,
                i.UnitPriceAmount))]);

        var utcNow = _timeProvider.GetUtcNow();

        var order = Order.CreateFromBasket(
            command.CorrelationId,
            command.BuyerId,
            basket,
            shippingAddressResult,
            billingAddressResult,
            command.PaymentMethodId,
            utcNow);

        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Order {OrderId} created from basket for Buyer {BuyerId} (CorrelationId {CorrelationId}); {ItemCount} items",
            order.Id, order.BuyerId, order.CorrelationId, order.Items.Count);

        return Result.Ok(order.Id);
    }

    private static CurrencyCode CreateCurrency(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 3)
        {
            throw new DataIntegrityException(
                "Order.InvalidCurrencyCode",
                $"Currency '{code}' is not a valid ISO 4217 code.");
        }

        if (!CurrencyCode.TryFromName(code.ToUpperInvariant(), out var currency))
        {
            throw new DataIntegrityException(
                "Order.UnknownCurrencyCode",
                $"Unknown ISO 4217 currency code '{code}'.");
        }

        return currency;
    }

    private static Address ToAddress(AddressInput input, string paramName)
    {
        var result = Address.Create(
            input.Street1,
            input.Street2,
            input.City,
            input.State,
            input.PostalCode,
            input.CountryCode);
        if (result.IsFailed)
        {
            throw new DataIntegrityException(
                $"Order.Invalid{paramName}",
                $"{paramName} failed validation: {string.Join("; ", result.Errors.Select(e => e.Message))}");
        }

        return result.Value;
    }
}
