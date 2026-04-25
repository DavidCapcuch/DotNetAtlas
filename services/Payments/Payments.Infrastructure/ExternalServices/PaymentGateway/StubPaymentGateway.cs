using FluentResults;
using Payments.Application.Abstractions;
using Payments.Domain.Errors;
using Payments.Domain.Transactions;
using Payments.Domain.Transactions.ValueObjects;
using Platform.SharedKernel.ValueObjects;

namespace Payments.Infrastructure.ExternalServices.PaymentGateway;

/// <summary>
/// Deterministic in-memory <see cref="IPaymentGateway"/> for tests, integration runs, and the
/// reference solution's local docker-compose. Real deployments swap in a Stripe / Adyen /
/// Braintree adapter via DI in <see cref="PaymentGatewayDependencyInjection"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Decline rule (teaching artefact):</b> any authorize call whose amount ends in
/// <c>.99</c> declines with <see cref="GatewayDeclinedError"/> carrying gateway code
/// <c>insufficient_funds</c>. Anchored in <c>docs/bc-design/example-mapping/payments.md § 2.1</c>;
/// it gives every example mapping session a deterministic anchor so integration tests do not
/// flake on time-of-day or random number behaviour. Capture / Void / Refund always succeed —
/// the rule applies on authorize only because reversal calls reference an already-validated
/// transaction id.
/// </para>
/// <para>
/// <b>Deterministic gateway-transaction-id:</b> derived as <c>$"stub-{tx.Id:N}"</c>. Pure
/// function of the aggregate's <c>PaymentId</c> — no clock, no <see cref="Guid"/> generator.
/// Tests can assert on the exact string without any DI plumbing.
/// </para>
/// <para>
/// <b>PCI scope:</b> the stub never sees PAN / CVV — only the tokenized
/// <see cref="PaymentTransaction.PaymentMethodId"/>. Mirrors the production token-only flow
/// per <c>docs/adr/0011-pii-handling-gdpr.md</c>.
/// </para>
/// <para>
/// <b>Stateless / singleton-safe:</b> no instance fields, no captured resources. Registered
/// as a singleton — see <see cref="PaymentGatewayDependencyInjection.AddPaymentGateway"/>.
/// </para>
/// </remarks>
internal sealed class StubPaymentGateway : IPaymentGateway
{
    /// <inheritdoc />
    public Task<Result<AuthorizeResponse>> AuthorizeAsync(PaymentTransaction tx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ct.ThrowIfCancellationRequested();

        if (EndsInNinetyNineCents(tx.Amount.Amount))
        {
            return Task.FromResult(
                Result.Fail<AuthorizeResponse>(new GatewayDeclinedError("insufficient_funds", "insufficient_funds")));
        }

        var response = new AuthorizeResponse(
            $"stub-{tx.Id:N}",
            new GatewayResponseCode("ok", "Approved"));

        return Task.FromResult(Result.Ok(response));
    }

    /// <inheritdoc />
    public Task<Result<CaptureResponse>> CaptureAsync(string gatewayTransactionId, Money amount, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayTransactionId);
        ArgumentNullException.ThrowIfNull(amount);
        ct.ThrowIfCancellationRequested();

        var response = new CaptureResponse(
            gatewayTransactionId,
            new GatewayResponseCode("ok", "Captured"));

        return Task.FromResult(Result.Ok(response));
    }

    /// <inheritdoc />
    public Task<Result<RefundResponse>> RefundAsync(string gatewayTransactionId, Money amount, string reason, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayTransactionId);
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ct.ThrowIfCancellationRequested();

        var response = new RefundResponse(new GatewayResponseCode("ok", "Refunded"));

        return Task.FromResult(Result.Ok(response));
    }

    /// <inheritdoc />
    public Task<Result<VoidResponse>> VoidAsync(string gatewayTransactionId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayTransactionId);
        ct.ThrowIfCancellationRequested();

        var response = new VoidResponse(new GatewayResponseCode("ok", "Voided"));

        return Task.FromResult(Result.Ok(response));
    }

    private static bool EndsInNinetyNineCents(decimal amount)
    {
        // Compare the fractional cents portion (rounded to 2dp) against 0.99 with epsilon
        // tolerance to absorb decimal-representation noise. Currencies with non-2dp scales
        // (JPY, KWD) will not match this rule, which is fine — the decline anchor is a
        // teaching artefact for USD/EUR-shaped integration tests.
        var fractional = Math.Round(amount - Math.Floor(amount), 2);
        return Math.Abs(fractional - 0.99m) < 0.0001m;
    }
}
