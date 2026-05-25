using FluentResults;
using Microsoft.EntityFrameworkCore;
using Ordering.Application.Common.Data;
using Ordering.Application.Orders.GetOrderById;
using Ordering.Domain.Orders;
using Platform.CQRS;

namespace Ordering.Application.Orders.GetOrdersByBuyer;

public sealed class GetOrdersByBuyerQueryHandler
    : IQueryHandler<GetOrdersByBuyerQuery, GetOrdersByBuyerResponse>
{
    private readonly IOrderingDbContext _dbContext;

    public GetOrdersByBuyerQueryHandler(IOrderingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<GetOrdersByBuyerResponse>> HandleAsync(
        GetOrdersByBuyerQuery query,
        CancellationToken ct)
    {
        var status = ParseStatus(query.Status);

        // SQL-side projection (#238, ADR-0021): selects only the columns the
        // response uses. Optional VOs are flat nullable columns on
        // `ordering.orders` and translate cleanly under conditional
        // projection (EF Core 10). Keep the projected shape in sync with
        // GetOrderByIdQueryHandler — both produce a byte-identical
        // GetOrderByIdResponse.
        var orders = await _dbContext.Orders
            .AsNoTracking()
            .Where(o => o.BuyerId == query.BuyerId)
            .Where(o => status == null || o.Status == status)
            .OrderByDescending(o => o.CreatedAtUtc)
            .ThenByDescending(o => o.Id)
            .Skip(query.Skip)
            .Take(query.Take)
            .TagWith(nameof(GetOrdersByBuyerQueryHandler))
            .Select(o => new GetOrderByIdResponse
            {
                OrderId = o.Id,
                BuyerId = o.BuyerId,
                CorrelationId = o.CorrelationId,
                PaymentMethodId = o.PaymentMethodId,
                Status = o.Status.Name,
                TotalAmount = o.Total.Amount,
                Currency = o.Total.Currency.Name,
                CreatedAtUtc = o.CreatedAtUtc,
                StockReservedAtUtc = o.StockReservedAtUtc,
                PaymentCompletedAtUtc = o.PaymentCompletedAtUtc,
                ConfirmedAtUtc = o.ConfirmedAtUtc,
                DeliveredAtUtc = o.DeliveredAtUtc,
                ShippingAddress = new AddressDto(
                    o.ShippingAddress.Street1,
                    o.ShippingAddress.Street2,
                    o.ShippingAddress.City,
                    o.ShippingAddress.State,
                    o.ShippingAddress.PostalCode,
                    o.ShippingAddress.CountryCode),
                BillingAddress = new AddressDto(
                    o.BillingAddress.Street1,
                    o.BillingAddress.Street2,
                    o.BillingAddress.City,
                    o.BillingAddress.State,
                    o.BillingAddress.PostalCode,
                    o.BillingAddress.CountryCode),
                Items = o.Items.Select(i => new OrderItemDto(
                    i.ProductId,
                    i.ProductSnapshot.Sku,
                    i.ProductSnapshot.Name,
                    i.Quantity,
                    i.UnitPrice.Amount,
                    i.LineTotal.Amount)).ToList(),
                Cancellation = o.Cancellation == null
                    ? null
                    : new CancellationDto(
                        o.Cancellation.Reason,
                        o.Cancellation.AtStatus.Name,
                        o.Cancellation.CancelledAtUtc),
                Failure = o.Failure == null
                    ? null
                    : new FailureDto(
                        o.Failure.ErrorCode,
                        o.Failure.ErrorMessage,
                        o.Failure.AtStatus.Name,
                        o.Failure.FailedAtUtc),
                Shipment = o.Shipment == null
                    ? null
                    : new ShipmentDto(
                        o.Shipment.Carrier,
                        o.Shipment.TrackingNumber,
                        o.Shipment.ShippedAtUtc),
            })
            .ToListAsync(ct);

        return Result.Ok(new GetOrdersByBuyerResponse
        {
            Orders = orders,
            Skip = query.Skip,
            Take = query.Take,
        });
    }

    private static OrderStatus? ParseStatus(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        // The validator is the front-line guard (see
        // GetOrdersByBuyerQueryValidator rule on Status). An unparseable
        // name reaching the handler is bug-class: validation was bypassed.
        if (!OrderStatus.TryFromName(name, out var status))
        {
            throw new Platform.SharedKernel.Exceptions.DataIntegrityException(
                "OrdersByBuyer.InvalidStatus",
                $"OrderStatus '{name}' did not parse; validator should have rejected this upstream.");
        }

        return status;
    }
}
