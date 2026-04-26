using FluentResults;
using Payments.Application.Common.Data;
using Payments.Domain.Errors;
using Platform.CQRS;

namespace Payments.Application.Transactions.GetPaymentById;

internal sealed class GetPaymentByIdQueryHandler : IQueryHandler<GetPaymentByIdQuery, GetPaymentByIdResponse>
{
    private readonly IPaymentRepository _repository;

    public GetPaymentByIdQueryHandler(IPaymentRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<GetPaymentByIdResponse>> HandleAsync(GetPaymentByIdQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var tx = await _repository.GetByIdAsync(query.PaymentId, ct);
        if (tx is null)
        {
            return Result.Fail<GetPaymentByIdResponse>(PaymentsErrors.PaymentNotFound(query.PaymentId));
        }

        return Result.Ok(tx.ToResponse());
    }
}
