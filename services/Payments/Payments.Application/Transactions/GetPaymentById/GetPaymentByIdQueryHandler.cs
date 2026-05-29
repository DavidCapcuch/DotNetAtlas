using FluentResults;
using Microsoft.EntityFrameworkCore;
using Payments.Application.Common.Data;
using Payments.Domain.Errors;
using Platform.CQRS;

namespace Payments.Application.Transactions.GetPaymentById;

internal sealed class GetPaymentByIdQueryHandler : IQueryHandler<GetPaymentByIdQuery, GetPaymentByIdResponse>
{
    private readonly IPaymentsDbContext _dbContext;

    public GetPaymentByIdQueryHandler(IPaymentsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<GetPaymentByIdResponse>> HandleAsync(GetPaymentByIdQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Read-side PK lookup (ADR-0021/0022): inline LINQ, AsNoTracking, no spec.
        var tx = await _dbContext.Transactions
            .AsNoTracking()
            .TagWith(nameof(GetPaymentByIdQueryHandler))
            .FirstOrDefaultAsync(t => t.Id == query.PaymentId, ct);
        if (tx is null)
        {
            return Result.Fail<GetPaymentByIdResponse>(PaymentsErrors.PaymentNotFound(query.PaymentId));
        }

        return Result.Ok(tx.ToResponse());
    }
}
