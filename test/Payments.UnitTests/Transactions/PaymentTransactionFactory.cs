using FluentResults;
using Payments.Domain.Transactions;
using Payments.Domain.Transactions.ValueObjects;
using Platform.SharedKernel.ValueObjects;

namespace Payments.UnitTests.Transactions;

/// <summary>
/// Shared helpers for constructing <see cref="PaymentTransaction"/> instances in specific
/// starting states, so aggregate tests can focus on the transition under test rather than
/// arrangement boilerplate.
/// </summary>
internal static class PaymentTransactionFactory
{
    public const string DefaultPaymentMethodId = "tok_visa_4242";
    public const string DefaultGatewayTransactionId = "gw-tx-abc123";

    public static readonly GatewayResponseCode SuccessResponse = new("ok", "Approved");
    public static readonly GatewayResponseCode DeclineResponse = new("insufficient_funds", "Declined");

    public static Money UsdAmount(decimal amount = 100m) => Money.Create(amount, "USD").Value;

    public static PaymentTransaction Requested(
        DateTimeOffset utcNow,
        decimal amount = 100m,
        string paymentMethodId = DefaultPaymentMethodId)
    {
        var result = PaymentTransaction.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            UsdAmount(amount),
            paymentMethodId,
            utcNow);

        if (result.IsFailed)
        {
            throw new InvalidOperationException($"Test setup failed: {string.Join("; ", result.Errors.Select(e => e.Message))}");
        }

        return result.Value;
    }

    public static PaymentTransaction Authorized(DateTimeOffset utcNow)
    {
        var tx = Requested(utcNow);
        tx.PopDomainEvents();
        tx.Authorize(DefaultGatewayTransactionId, SuccessResponse, utcNow.AddDays(7), utcNow);
        tx.PopDomainEvents();
        return tx;
    }

    public static PaymentTransaction Completed(DateTimeOffset utcNow)
    {
        var tx = Authorized(utcNow);
        tx.Capture(DefaultGatewayTransactionId, SuccessResponse, utcNow);
        tx.PopDomainEvents();
        return tx;
    }

    public static PaymentTransaction Failed(DateTimeOffset utcNow)
    {
        var tx = Requested(utcNow);
        tx.PopDomainEvents();
        tx.MarkAuthorizationFailed(new FailureInfo(FailureReason.InsufficientFunds, "insufficient_funds", utcNow), utcNow);
        tx.PopDomainEvents();
        return tx;
    }

    public static PaymentTransaction Voided(DateTimeOffset utcNow)
    {
        var tx = Authorized(utcNow);
        tx.Void(SuccessResponse, utcNow);
        tx.PopDomainEvents();
        return tx;
    }

    public static PaymentTransaction Refunded(DateTimeOffset utcNow)
    {
        var tx = Completed(utcNow);
        tx.Refund("customer_cancelled", SuccessResponse, utcNow);
        tx.PopDomainEvents();
        return tx;
    }
}
