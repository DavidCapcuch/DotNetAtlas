using FluentResults;
using Microsoft.EntityFrameworkCore;
using Payments.Domain.Transactions;
using Payments.Domain.Transactions.ValueObjects;
using Payments.Infrastructure.Persistence.Database;
using Platform.SharedKernel.ValueObjects;

namespace Payments.IntegrationTests.Common;

/// <summary>
/// Builds each <see cref="PaymentTransaction"/> through the aggregate's factory and
/// transitions so its invariants hold, and pops the raised domain events before saving
/// so seeding never runs an outbox publisher.
/// </summary>
internal static class PaymentSeed
{
    private const decimal DefaultAmount = 49.99m;
    private const string DefaultCurrency = "USD";
    private const string DefaultPaymentMethodId = "pm_test_card_visa";

    public static Task<PaymentTransaction> InsertRequestedAsync(
        PaymentsDbContext dbContext,
        Guid? paymentId = null,
        Guid? orderId = null,
        decimal amount = DefaultAmount,
        string currency = DefaultCurrency,
        string paymentMethodId = DefaultPaymentMethodId) =>
        SaveDetachedAsync(dbContext, CreateRequested(paymentId, orderId, amount, currency, paymentMethodId));

    public static Task<PaymentTransaction> InsertAuthorizedAsync(
        PaymentsDbContext dbContext,
        string gatewayTransactionId,
        string gatewayResponseCode,
        DateTimeOffset authorizedAtUtc)
    {
        var aggregate = CreateRequested(null, null, DefaultAmount, DefaultCurrency, DefaultPaymentMethodId);
        ThrowIfFailed(
            aggregate.Authorize(
                gatewayTransactionId,
                GatewayResponseCode.Create(gatewayResponseCode, "Approved"),
                expiresAtUtc: authorizedAtUtc.AddDays(7),
                utcNow: authorizedAtUtc),
            "authorize the PaymentTransaction");
        return SaveDetachedAsync(dbContext, aggregate);
    }

    public static Task<PaymentTransaction> InsertFailedAsync(PaymentsDbContext dbContext, FailureInfo failure)
    {
        var aggregate = CreateRequested(null, null, DefaultAmount, DefaultCurrency, DefaultPaymentMethodId);
        ThrowIfFailed(
            aggregate.MarkAuthorizationFailed(failure, utcNow: failure.RecordedAtUtc),
            "fail the PaymentTransaction's authorization");
        return SaveDetachedAsync(dbContext, aggregate);
    }

    private static PaymentTransaction CreateRequested(
        Guid? paymentId,
        Guid? orderId,
        decimal amount,
        string currency,
        string paymentMethodId)
    {
        var moneyResult = Money.Create(amount, currency);
        ThrowIfFailed(moneyResult, "produce valid Money");

        var aggregateResult = PaymentTransaction.Create(
            paymentId: paymentId ?? Guid.CreateVersion7(),
            buyerId: Guid.CreateVersion7(),
            orderId: orderId ?? Guid.CreateVersion7(),
            amount: moneyResult.Value,
            paymentMethodId: paymentMethodId);
        ThrowIfFailed(aggregateResult, "create the PaymentTransaction");

        return aggregateResult.Value;
    }

    private static async Task<PaymentTransaction> SaveDetachedAsync(
        PaymentsDbContext dbContext,
        PaymentTransaction aggregate)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _ = aggregate.PopDomainEvents();
        dbContext.Transactions.Add(aggregate);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        // Detach so subsequent reads in the same scope hit the database, not the change tracker.
        dbContext.Entry(aggregate).State = EntityState.Detached;
        return aggregate;
    }

    private static void ThrowIfFailed(ResultBase result, string step)
    {
        if (result.IsFailed)
        {
            throw new InvalidOperationException(
                $"Test seed could not {step}: {string.Join("; ", result.Errors.Select(e => e.Message))}");
        }
    }
}
