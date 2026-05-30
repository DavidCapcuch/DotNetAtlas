using FluentResults;
using Microsoft.EntityFrameworkCore;
using Payments.Application.Common.Data;
using Payments.Application.Transactions;
using Platform.CQRS;

namespace Payments.Application.Transactions.GetPaymentsByOrder;

internal sealed class GetPaymentsByOrderQueryHandler : IQueryHandler<GetPaymentsByOrderQuery, GetPaymentsByOrderResponse>
{
    private readonly IPaymentsDbContext _dbContext;

    public GetPaymentsByOrderQueryHandler(IPaymentsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<GetPaymentsByOrderResponse>> HandleAsync(GetPaymentsByOrderQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Read-side list query (ADR-0021/0022): inline LINQ, AsNoTracking, deterministic order,
        // SQL-side projection so the full aggregate is never materialised.
        var rows = await _dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.OrderId == query.OrderId)
            .OrderBy(t => t.Id)
            .TagWith(nameof(GetPaymentsByOrderQueryHandler))
            .Select(PaymentTransactionRow.Projection)
            .ToListAsync(ct);

        return Result.Ok(new GetPaymentsByOrderResponse
        {
            OrderId = query.OrderId,
            Payments = rows.Select(static row => row.ToResponse()).ToList(),
        });
    }
}
