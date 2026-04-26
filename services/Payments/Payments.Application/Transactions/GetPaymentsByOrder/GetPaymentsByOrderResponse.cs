using Payments.Application.Transactions.GetPaymentById;

namespace Payments.Application.Transactions.GetPaymentsByOrder;

public sealed record GetPaymentsByOrderResponse
{
    public required Guid OrderId { get; init; }

    public required IReadOnlyList<GetPaymentByIdResponse> Payments { get; init; }
}
