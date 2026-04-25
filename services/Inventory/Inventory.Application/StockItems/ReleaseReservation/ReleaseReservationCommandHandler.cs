using FluentResults;
using Inventory.Application.Common.Data;
using Inventory.Domain.StockItems.ValueObjects;
using Microsoft.Extensions.Logging;
using Platform.CQRS;

namespace Inventory.Application.StockItems.ReleaseReservation;

internal sealed class ReleaseReservationCommandHandler : ICommandHandler<ReleaseReservationCommand>
{
    private readonly IEventStore _eventStore;
    private readonly ILogger<ReleaseReservationCommandHandler> _logger;

    public ReleaseReservationCommandHandler(
        IEventStore eventStore,
        ILogger<ReleaseReservationCommandHandler> logger)
    {
        _eventStore = eventStore;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(ReleaseReservationCommand command, CancellationToken ct)
    {
        var reservationIdResult = ReservationId.Create(command.ReservationId);
        if (reservationIdResult.IsFailed)
        {
            return reservationIdResult.ToResult();
        }

        var result = await _eventStore.AppendAsync(
            streamId: command.ProductId,
            command: aggregate => aggregate.ReleaseReservation(
                reservationIdResult.Value,
                command.Reason,
                command.OccurredOnUtc),
            correlationId: command.CorrelationId,
            ct: ct).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            _logger.LogInformation(
                "Released reservation {ReservationId} on Product {ProductId} (reason={Reason}, version after append: {Version})",
                command.ReservationId, command.ProductId, command.Reason, result.Value.Version);
        }

        return result.ToResult();
    }
}
