using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ordering.Application.Common.Data;
using Ordering.Domain.Errors;
using Platform.CQRS;

namespace Ordering.Application.Orders.GetOrderById;

public sealed class GetOrderByIdQueryHandler : IQueryHandler<GetOrderByIdQuery, GetOrderByIdResponse>
{
    private readonly IOrderingDbContext _dbContext;
    private readonly ILogger<GetOrderByIdQueryHandler> _logger;

    public GetOrderByIdQueryHandler(
        IOrderingDbContext dbContext,
        ILogger<GetOrderByIdQueryHandler> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<Result<GetOrderByIdResponse>> HandleAsync(
        GetOrderByIdQuery query,
        CancellationToken ct)
    {
        // SQL-side projection (ADR-0021 / #277): only the columns the response uses
        // travel from DB. Optional VOs are flat nullable columns on `ordering.orders`
        // and translate cleanly under conditional projection (EF Core 10). This handler
        // returns the full order shape; the buyer-list endpoint deliberately returns a
        // narrower summary (use-cases.md § 3.4.2), so the two projections are not kept
        // in sync — they are intentionally divergent.
        var response = await _dbContext.Orders
            .AsNoTracking()
            .Where(o => o.Id == query.OrderId)
            .TagWith(nameof(GetOrderByIdQueryHandler))
            .Select(o => new GetOrderByIdResponse
            {
                OrderId = o.Id,
                BuyerId = o.BuyerId,
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
            .FirstOrDefaultAsync(ct);

        if (response is null)
        {
            return Result.Fail<GetOrderByIdResponse>(OrderingErrors.OrderNotFound(query.OrderId));
        }

        // Ownership enforcement: buyer may read only their own order. Return NotFound
        // (not Forbidden) for a cross-buyer lookup so existence is not leaked.
        // Logged at Warning so SecOps can probe for credential-stuffing patterns.
        if (!query.IsAdmin && response.BuyerId != query.BuyerId)
        {
            _logger.LogWarning(
                "Buyer {BuyerId} requested order {OrderId} owned by a different buyer — returning NotFound",
                query.BuyerId, query.OrderId);
            return Result.Fail<GetOrderByIdResponse>(OrderingErrors.OrderNotFound(query.OrderId));
        }

        return Result.Ok(response);
    }
}
