using FluentResults;
using Payments.Domain.Errors;
using Payments.Domain.Transactions;
using Platform.SharedKernel.ValueObjects;

namespace Payments.Application.Abstractions;

/// <summary>
/// Port over the external payment processor (Stripe / Adyen / Braintree in production;
/// <c>StubPaymentGateway</c> in v1). Lives in <c>Payments.Application</c>; concrete adapters
/// live in <c>Payments.Infrastructure</c>. Locked at the seams per
/// <c>docs/bc-design/payments.md § 10</c>.
/// </summary>
/// <remarks>
/// Failure semantics:
/// <list type="bullet">
///   <item><description>Business-expected gateway declines (e.g., <c>insufficient_funds</c>,
///   <c>card_declined</c>, <c>fraud_suspected</c>) → <see cref="Result.Fail{T}(IError)"/> with a
///   <see cref="GatewayDeclinedError"/>. The handler translates this into
///   <see cref="Payments.Domain.Transactions.ValueObjects.FailureInfo"/> and calls
///   <see cref="PaymentTransaction.MarkAuthorizationFailed"/> /
///   <see cref="PaymentTransaction.MarkCaptureFailed"/>.</description></item>
///   <item><description>Infrastructure faults (timeout, gateway unreachable) → throw or surface
///   as a <see cref="Platform.SharedKernel.Errors.ValidationError"/> (<c>Payments.GatewayUnavailable</c>);
///   handler converts to a 503 / saga retry per ADR-0010.</description></item>
/// </list>
/// PCI scope minimization: every operation references a tokenized
/// <see cref="PaymentTransaction.PaymentMethodId"/> and a gateway-issued
/// <c>GatewayTransactionId</c>. PAN / CVV never enter Payments — see
/// <c>docs/adr/0011-pii-handling-gdpr.md</c>.
/// </remarks>
public interface IPaymentGateway
{
    /// <summary>
    /// Authorizes the payment by holding the funds at the gateway.
    /// </summary>
    /// <param name="tx">Aggregate carrying amount + tokenized payment method.</param>
    /// <param name="idempotencyKey">Saga-issued idempotency key — a v2 real adapter (Stripe,
    /// Adyen) forwards this as the gateway's <c>Idempotency-Key</c> header so the gateway-side
    /// dedups duplicate authorize attempts during the "SaveChanges fails post-gateway"
    /// recovery path.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Authorize response with the gateway-issued transaction id.</returns>
    Task<Result<AuthorizeResponse>> AuthorizeAsync(PaymentTransaction tx, string idempotencyKey, CancellationToken ct);

    /// <summary>
    /// Captures (settles) a previously authorized transaction.
    /// </summary>
    /// <param name="gatewayTransactionId">Token returned from <see cref="AuthorizeAsync"/>.</param>
    /// <param name="amount">Amount to capture.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Capture response.</returns>
    Task<Result<CaptureResponse>> CaptureAsync(string gatewayTransactionId, Money amount, CancellationToken ct);

    /// <summary>
    /// Refunds a captured transaction (post-capture compensation).
    /// </summary>
    /// <param name="gatewayTransactionId">Token from the original authorize/capture.</param>
    /// <param name="amount">Amount to refund.</param>
    /// <param name="reason">Saga's reason for the refund (audit).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Refund response.</returns>
    Task<Result<RefundResponse>> RefundAsync(string gatewayTransactionId, Money amount, string reason, CancellationToken ct);

    /// <summary>
    /// Voids an authorized-but-not-yet-captured transaction (pre-capture compensation).
    /// </summary>
    /// <param name="gatewayTransactionId">Token from the original authorize.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Void response.</returns>
    Task<Result<VoidResponse>> VoidAsync(string gatewayTransactionId, CancellationToken ct);
}
