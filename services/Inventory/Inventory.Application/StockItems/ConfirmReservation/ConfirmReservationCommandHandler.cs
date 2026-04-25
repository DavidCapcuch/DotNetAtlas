using FluentResults;
using Inventory.Application.Common.Data;
using Inventory.Domain.StockItems.ValueObjects;
using Microsoft.Extensions.Logging;
using Platform.CQRS;

namespace Inventory.Application.StockItems.ConfirmReservation;

internal sealed class ConfirmReservationCommandHandler : ICommandHandler<ConfirmReservationCommand>
{
    private readonly IEventStore _eventStore;
    private readonly ILogger<ConfirmReservationCommandHandler> _logger;

    public ConfirmReservationCommandHandler(
        IEventStore eventStore,
        ILogger<ConfirmReservationCommandHandler> logger)
    {
        _eventStore = eventStore;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(ConfirmReservationCommand command, CancellationToken ct)
    {
        var reservationIdResult = ReservationId.Create(command.ReservationId);
        if (reservationIdResult.IsFailed)
        {
            return reservationIdResult.ToResult();
        }

        var result = await _eventStore.AppendAsync(
            streamId: command.ProductId,
            command: aggregate => aggregate.ConfirmReservation(
                reservationIdResult.Value,
                command.OccurredOnUtc),
            correlationId: command.CorrelationId,
            ct: ct).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            _logger.LogInformation(
                "Confirmed reservation {ReservationId} on Product {ProductId} (version after append: {Version})",
                command.ReservationId, command.ProductId, result.Value.Version);
        }

        return result.ToResult();
    }
}
