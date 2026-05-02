using Microsoft.EntityFrameworkCore;
using Payments.Domain.Transactions;
using Payments.Infrastructure.Persistence.Database;
using Platform.SharedKernel.ValueObjects;

namespace Payments.FunctionalTests.Common;

/// <summary>
/// Seed helpers for Payments functional tests — inserts a
/// <see cref="PaymentTransaction"/> directly via the DbContext so the GET
/// endpoints have something to read. Uses the public <c>Create</c> factory so
/// invariants (positive amount, valid payment-method token) are honoured.
/// </summary>
internal static class PaymentSeed
{
    public static async Task<PaymentTransaction> InsertRequestedAsync(
        PaymentsDbContext dbContext,
        DateTimeOffset utcNow,
        Guid? paymentId = null,
        Guid? orderId = null,
        decimal amount = 49.99m,
        string currency = "USD",
        string paymentMethodId = "pm_test_card_visa")
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var moneyResult = Money.Create(amount, currency);
        if (moneyResult.IsFailed)
        {
            throw new InvalidOperationException(
                $"Test seed produced invalid Money: {string.Join("; ", moneyResult.Errors.Select(e => e.Message))}");
        }

        var aggregateResult = PaymentTransaction.Create(
            paymentId: paymentId ?? Guid.CreateVersion7(),
            correlationId: Guid.CreateVersion7(),
            buyerId: Guid.CreateVersion7(),
            orderId: orderId ?? Guid.CreateVersion7(),
            amount: moneyResult.Value,
            paymentMethodId: paymentMethodId,
            utcNow: utcNow);

        if (aggregateResult.IsFailed)
        {
            throw new InvalidOperationException(
                $"Test seed could not create PaymentTransaction: {string.Join("; ", aggregateResult.Errors.Select(e => e.Message))}");
        }

        var aggregate = aggregateResult.Value;
        dbContext.Transactions.Add(aggregate);
        await dbContext.SaveChangesAsync();
        // Detach so subsequent reads in the same scope hit the database, not the change tracker.
        dbContext.Entry(aggregate).State = EntityState.Detached;
        return aggregate;
    }
}
