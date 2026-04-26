using FluentResults;
using Payments.Application.Common.Data;
using Payments.Application.Transactions;
using Platform.CQRS;

namespace Payments.Application.Transactions.GetPaymentsByOrder;

internal sealed class GetPaymentsByOrderQueryHandler : IQueryHandler<GetPaymentsByOrderQuery, GetPaymentsByOrderResponse>
{
    private readonly IPaymentRepository _repository;

    public GetPaymentsByOrderQueryHandler(IPaymentRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<GetPaymentsByOrderResponse>> HandleAsync(GetPaymentsByOrderQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var rows = await _repository.GetByOrderIdAsync(query.OrderId, ct);

        return Result.Ok(new GetPaymentsByOrderResponse
        {
            OrderId = query.OrderId,
            Payments = rows.Select(static tx => tx.ToResponse()).ToList(),
        });
    }
}
