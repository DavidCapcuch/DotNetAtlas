using System.Net;
using FastEndpoints;
using Ordering.Api.Common.Extensions;
using Ordering.Application.Orders.CancelOrder;
using Platform.Api.Extensions;
using Serilog.Context;

namespace Ordering.Api.Endpoints.Orders.CancelOrder;

/// <summary>
/// <c>POST /api/v1/ordering/orders/{orderId}/cancel</c> — buyer or admin
/// cancellation request. Authorisation is dual-mode: buyers may cancel only
/// their own orders (cross-buyer attempts surface as 404 to avoid existence
/// leak per <c>ordering.md § 9.2</c>); admins (Keycloak realm role
/// <c>admin</c>) may cancel any order. Cancellation after <c>Shipped</c>
/// fails with 409 (I-12).
/// </summary>
/// <remarks>
/// FastEndpoints' built-in <c>.Idempotency()</c> filter (per ADR-0013) is
/// attached so a double-clicked admin cancel returns the same 204 from the
/// Redis-backed output cache instead of running the handler twice. ADR-0013's
/// worked example sets <c>AdditionalCacheKey</c>, but FastEndpoints 7.0.1's
/// <see cref="IdempotencyOptions"/> does not surface that property — instead
/// it ships <c>Authorization</c> in the default
/// <see cref="IdempotencyOptions.AdditionalHeaders"/>, which the
/// <c>OutputCachePolicy</c> wires into <c>CacheVaryByRules.HeaderNames</c>.
/// Net effect: the cache slot varies by bearer token, so two different
/// buyers reusing the same UUID never share responses. The cross-buyer
/// partition is pinned by
/// <c>WhenSameIdempotencyKeyUsedByDifferentBuyer_HandlerStillRuns</c>
/// in the functional test suite — if a future FastEndpoints minor drops
/// Authorization from the defaults, that test fails loudly.
/// </remarks>
internal sealed class CancelOrderEndpoint : Endpoint<CancelOrderRequest>
{
    private readonly Platform.CQRS.ICommandHandler<CancelOrderCommand> _handler;

    public CancelOrderEndpoint(Platform.CQRS.ICommandHandler<CancelOrderCommand> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Post("{OrderId}/cancel");
        Version(1);
        Group<OrdersGroup>();
        Idempotency(opts =>
        {
            // Header name + 24-hour TTL are the ADR-0013 § Implementation
            // Notes contract. The default Authorization-header inclusion in
            // IdempotencyOptions.AdditionalHeaders already partitions per
            // buyer, so no AdditionalCacheKey is needed.
            opts.HeaderName = "Idempotency-Key";
            opts.CacheDuration = TimeSpan.FromHours(24);
        });
        Summary(s =>
        {
            s.Summary = "Cancel an order. Buyer may cancel their own order; admins may cancel any.";
            s.ExampleRequest = new CancelOrderRequest
            {
                OrderId = new Guid("0198B2A9-CB8C-744B-8CDD-0B64727CF2FC"),
                Reason = "changed mind",
            };
        });
        Description(b =>
        {
            b.Produces((int)HttpStatusCode.NoContent);
            b.Produces((int)HttpStatusCode.BadRequest);
            b.Produces((int)HttpStatusCode.Unauthorized);
            b.Produces((int)HttpStatusCode.NotFound);
            b.Produces((int)HttpStatusCode.Conflict);
            b.Produces((int)HttpStatusCode.UnprocessableEntity);
        });
    }

    public override async Task HandleAsync(CancelOrderRequest req, CancellationToken ct)
    {
        var buyerId = User.GetBuyerIdOrNull();
        var isAdmin = User.IsOrderingAdmin();

        // Buyers must have a parseable sub. Admins may legitimately have a
        // service-account token whose sub is not a Guid; for them we set
        // BuyerId=Guid.Empty (the application command's IsAdmin branch
        // ignores BuyerId).
        if (!isAdmin && buyerId is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        using var _ = LogContext.PushProperty("OrderId", req.OrderId);
        using var __ = LogContext.PushProperty("IsAdmin", isAdmin);

        var command = new CancelOrderCommand
        {
            OrderId = req.OrderId,
            Reason = req.Reason,
            BuyerId = buyerId ?? Guid.Empty,
            IsAdmin = isAdmin,
        };

        var result = await _handler.HandleAsync(command, ct);

        await result.MatchAsync(
            () => Send.NoContentAsync(ct),
            failure => Send.SendErrorResponseAsync(failure, ct));
    }
}
