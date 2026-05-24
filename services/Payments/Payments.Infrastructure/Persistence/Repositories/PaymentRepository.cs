using Microsoft.EntityFrameworkCore;
using Payments.Application.Common.Data;
using Payments.Domain.Transactions;
using Payments.Infrastructure.Persistence.Database;

namespace Payments.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IPaymentRepository"/>. Adapts the application-layer port
/// over <see cref="PaymentsDbContext.Transactions"/>; the only place EF LINQ runs against the
/// aggregate set.
/// </summary>
internal sealed class PaymentRepository : IPaymentRepository
{
    private readonly PaymentsDbContext _dbContext;

    public PaymentRepository(PaymentsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public Task<PaymentTransaction?> GetByIdForUpdateAsync(Guid paymentId, CancellationToken ct) =>
        _dbContext.Transactions.FirstOrDefaultAsync(t => t.Id == paymentId, ct);

    /// <inheritdoc />
    public Task<PaymentTransaction?> GetByIdAsNoTrackingAsync(Guid paymentId, CancellationToken ct) =>
        _dbContext.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == paymentId, ct);

    /// <inheritdoc />
    public Task<PaymentTransaction?> GetByCorrelationIdForUpdateAsync(Guid correlationId, CancellationToken ct) =>
        _dbContext.Transactions.FirstOrDefaultAsync(t => t.CorrelationId == correlationId, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PaymentTransaction>> GetByOrderIdAsync(Guid orderId, CancellationToken ct) =>
        await _dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.OrderId == orderId)
            .OrderBy(t => t.Id)
            .ToListAsync(ct);

    /// <inheritdoc />
    public void Add(PaymentTransaction paymentTransaction)
    {
        ArgumentNullException.ThrowIfNull(paymentTransaction);
        _dbContext.Transactions.Add(paymentTransaction);
    }
}
