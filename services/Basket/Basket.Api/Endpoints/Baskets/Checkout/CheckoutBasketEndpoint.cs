using System.Net;
using Basket.Api.Common.Extensions;
using Basket.Application.Baskets.Checkout;
using FastEndpoints;

namespace Basket.Api.Endpoints.Baskets.Checkout;

internal sealed class CheckoutBasketEndpoint : Endpoint<CheckoutBasketRequest, CheckoutBasketResponse>
{
    private readonly Platform.CQRS.ICommandHandler<CheckoutBasketCommand, Guid> _handler;

    public CheckoutBasketEndpoint(Platform.CQRS.ICommandHandler<CheckoutBasketCommand, Guid> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Post("checkout");
        Version(1);
        Group<BasketGroup>();
        Idempotency(opts =>
        {
            // Header name + 24-hour TTL match ADR-0013 § Implementation Notes.
            // FastEndpoints 7.0.1 ships Authorization in IdempotencyOptions.AdditionalHeaders
            // by default, which the OutputCachePolicy wires into CacheVaryByRules.HeaderNames
            // — so two different users reusing the same UUID never share responses.
            // The cross-user partition is pinned by
            // CheckoutBasketTests.WhenSameIdempotencyKeyUsedByDifferentUser_HandlerStillRuns;
            // a future FE minor that drops Authorization from the defaults fails that test
            // loudly.
            opts.HeaderName = "Idempotency-Key";
            opts.CacheDuration = TimeSpan.FromHours(24);
        });
        Summary(s =>
        {
            s.Summary = "Initiate checkout. Idempotency-Key header is required (ADR-0013).";
        });
        Description(b =>
        {
            b.Produces<CheckoutBasketResponse>((int)HttpStatusCode.Accepted);
            b.Produces((int)HttpStatusCode.BadRequest);
            b.Produces((int)HttpStatusCode.Unauthorized);
            b.Produces((int)HttpStatusCode.NotFound);
            b.Produces((int)HttpStatusCode.Conflict);
        });
    }

    public override async Task HandleAsync(CheckoutBasketRequest request, CancellationToken ct)
    {
        // ADR-0013 makes the Idempotency-Key header REQUIRED on /checkout. FastEndpoints
        // 7.0.1's built-in .Idempotency() filter only enables response caching when the
        // header is present; in this BC's wiring it does NOT 400 on absence (verified
        // empirically by CheckoutBasketTests.WhenIdempotencyKeyMissing_Returns400). We
        // enforce the contract explicitly here so client retries cannot silently bypass
        // the dedupe path.
        if (!HttpContext.Request.Headers.ContainsKey("Idempotency-Key"))
        {
            AddError(
                "Idempotency-Key header is required (ADR-0013).",
                "Basket.IdempotencyKeyMissing");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var userId = User.GetUserIdFromSubClaim();
        var command = new CheckoutBasketCommand(
            UserId: userId,
            CorrelationId: request.CorrelationId,
            ShippingAddress: request.ShippingAddress,
            BillingAddress: request.BillingAddress,
            PaymentMethodId: request.PaymentMethodId);

        var result = await _handler.HandleAsync(command, ct);

        await result.MatchAsync(
            correlationId => Send.ResponseAsync(
                new CheckoutBasketResponse { CorrelationId = correlationId },
                statusCode: StatusCodes.Status202Accepted,
                cancellation: ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}
