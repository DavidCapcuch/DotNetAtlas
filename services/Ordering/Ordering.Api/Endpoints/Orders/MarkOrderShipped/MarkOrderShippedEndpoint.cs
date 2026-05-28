using System.Net;
using FastEndpoints;
using Ordering.Api.Common.Authorization;
using Ordering.Api.Common.Extensions;
using Ordering.Application.Orders.MarkOrderShipped;
using Platform.Api.Extensions;
using Serilog.Context;

namespace Ordering.Api.Endpoints.Orders.MarkOrderShipped;

/// <summary>
/// <c>POST /api/v1/ordering/orders/{orderId}/ship</c> — admin/warehouse-only
/// transition to <c>OrderStatus.Shipped</c>. Authorisation is gated by
/// <see cref="AuthPolicies.OrderingAdmin"/>; non-admins receive 403.
/// </summary>
/// <remarks>
/// FSM violations on this endpoint are bug-class — <c>Order.MarkShipped</c>
/// uses <c>GuardTransition</c> rather than <c>OrderingErrors.*</c> because
/// the saga (or admin tooling) is expected to gate inputs. An order not in
/// <c>Confirmed</c> reaching this endpoint surfaces as 5xx via the platform
/// exception handler — not 409. <c>Cancel</c> is the one Ordering action
/// with a user-facing FSM-rejection error (<c>OrderingErrors.CannotCancelInStatus</c>);
/// see <c>ordering.md § 9.4</c> + Order.cs:325-364.
/// </remarks>
internal sealed class MarkOrderShippedEndpoint
    : Endpoint<MarkOrderShippedRequest>
{
    private readonly Platform.CQRS.ICommandHandler<MarkOrderShippedCommand> _handler;

    public MarkOrderShippedEndpoint(Platform.CQRS.ICommandHandler<MarkOrderShippedCommand> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Post("{OrderId}/ship");
        Version(1);
        Group<OrdersGroup>();
        Policies(AuthPolicies.OrderingAdmin);
        Summary(s =>
        {
            s.Summary = "Mark an order shipped (admin/warehouse-operator).";
            s.ExampleRequest = new MarkOrderShippedRequest
            {
                OrderId = new Guid("0198B2A9-CB8C-744B-8CDD-0B64727CF2FC"),
                Carrier = "DHL",
                TrackingNumber = "1Z999AA10123456784",
            };
        });
        Description(b =>
        {
            b.Produces((int)HttpStatusCode.NoContent);
            b.Produces((int)HttpStatusCode.Unauthorized);
            b.Produces((int)HttpStatusCode.Forbidden);
            b.Produces((int)HttpStatusCode.NotFound);
            b.Produces((int)HttpStatusCode.UnprocessableEntity);
        });
    }

    public override async Task HandleAsync(MarkOrderShippedRequest req, CancellationToken ct)
    {
        using var _ = LogContext.PushProperty("OrderId", req.OrderId);

        var command = new MarkOrderShippedCommand
        {
            OrderId = req.OrderId,
            Carrier = req.Carrier,
            TrackingNumber = req.TrackingNumber,
        };

        var result = await _handler.HandleAsync(command, ct);

        await result.MatchAsync(
            () => Send.NoContentAsync(ct),
            failure => Send.SendErrorResponseAsync(failure, ct));
    }
}
