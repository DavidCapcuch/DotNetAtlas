using System.Net;
using FastEndpoints;
using Ordering.API.Common.Extensions;
using Ordering.Application.Orders.MarkOrderDelivered;
using Ordering.Infrastructure.Common.Authorization;
using Serilog.Context;

namespace Ordering.API.Endpoints.Orders.MarkOrderDelivered;

/// <summary>
/// <c>POST /api/v1/ordering/orders/{orderId}/deliver</c> — admin-only
/// transition to <c>OrderStatus.Delivered</c> (the happy-path terminal
/// state). v2 may replace this surface with a carrier-webhook adapter
/// (<c>ordering.md Appendix B.6</c>).
/// </summary>
/// <remarks>
/// FSM violations are bug-class (same reasoning as <c>MarkOrderShippedEndpoint</c>):
/// the admin tooling is expected to call this only on <c>Shipped</c>
/// orders. An out-of-state delivery surfaces as 5xx, not 409.
/// </remarks>
internal sealed class MarkOrderDeliveredEndpoint
    : Endpoint<MarkOrderDeliveredRequest>
{
    private readonly Platform.CQRS.ICommandHandler<MarkOrderDeliveredCommand> _handler;

    public MarkOrderDeliveredEndpoint(Platform.CQRS.ICommandHandler<MarkOrderDeliveredCommand> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Post("{OrderId}/deliver");
        Version(1);
        Group<OrdersGroup>();
        Policies(AuthPolicies.OrderingAdmin);
        Summary(s =>
        {
            s.Summary = "Mark an order delivered (admin).";
            s.ExampleRequest = new MarkOrderDeliveredRequest
            {
                OrderId = new Guid("0198B2A9-CB8C-744B-8CDD-0B64727CF2FC"),
            };
        });
        Description(b =>
        {
            b.Produces((int)HttpStatusCode.NoContent);
            b.Produces((int)HttpStatusCode.Unauthorized);
            b.Produces((int)HttpStatusCode.Forbidden);
            b.Produces((int)HttpStatusCode.NotFound);
        });
    }

    public override async Task HandleAsync(MarkOrderDeliveredRequest req, CancellationToken ct)
    {
        using var _ = LogContext.PushProperty("OrderId", req.OrderId);

        var command = new MarkOrderDeliveredCommand
        {
            OrderId = req.OrderId,
        };

        var result = await _handler.HandleAsync(command, ct);

        await result.MatchAsync(
            () => Send.NoContentAsync(ct),
            failure => Send.SendErrorResponseAsync(failure, ct));
    }
}
