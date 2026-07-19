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
/// <b>Singleton-safe:</b> only captured resource is the injected <see cref="TimeProvider"/>;
/// registered as a singleton — see
/// <see cref="PaymentGatewayDependencyInjection.AddPaymentGateway"/>. The stub uses the time
/// provider to set <see cref="AuthorizeResponse.ExpiresAtUtc"/> as <c>now + 7 days</c>; a real
/// PSP adapter (v2) reads the actual expiry off the gateway response.
/// </para>
/// </remarks>
internal sealed class StubPaymentGateway : IPaymentGateway
{
    /// <summary>
    /// Sentinel authorization-expiry window. Lives in the adapter (not the mapper) so a v2 real
    /// adapter can swap in the gateway-issued expiry without touching the Application layer.
    /// </summary>
    private static readonly TimeSpan AuthorizationLifetime = TimeSpan.FromDays(7);

    private readonly TimeProvider _timeProvider;

    public StubPaymentGateway(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task<Result<AuthorizeResponse>> AuthorizeAsync(PaymentTransaction tx, string idempotencyKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ct.ThrowIfCancellationRequested();

        if (EndsInNinetyNineCents(tx.Amount.Amount))
        {
            return Task.FromResult(
                Result.Fail<AuthorizeResponse>(new GatewayDeclinedError("Insufficient funds on file", "insufficient_funds")));
        }

        var response = new AuthorizeResponse(
            $"stub-{tx.Id:N}",
            GatewayResponseCode.Create("ok", "Approved"),
            _timeProvider.GetUtcNow().Add(AuthorizationLifetime));

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
            GatewayResponseCode.Create("ok", "Captured"));

        return Task.FromResult(Result.Ok(response));
    }

    /// <inheritdoc />
    public Task<Result<RefundResponse>> RefundAsync(string gatewayTransactionId, Money amount, string reason, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayTransactionId);
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ct.ThrowIfCancellationRequested();

        var response = new RefundResponse(GatewayResponseCode.Create("ok", "Refunded"));

        return Task.FromResult(Result.Ok(response));
    }

    /// <inheritdoc />
    public Task<Result<VoidResponse>> VoidAsync(string gatewayTransactionId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayTransactionId);
        ct.ThrowIfCancellationRequested();

        var response = new VoidResponse(GatewayResponseCode.Create("ok", "Voided"));

        return Task.FromResult(Result.Ok(response));
    }

    private static bool EndsInNinetyNineCents(decimal amount)
    {
        // Decimal has no IEEE rounding noise, so a direct equality on the 2dp fractional portion
        // is sound (#264). Currencies with non-2dp scales (JPY, KWD) will not match this rule,
        // which is fine — the decline anchor is a teaching artefact for USD/EUR-shaped
        // integration tests.
        return Math.Round(amount - Math.Floor(amount), 2) == 0.99m;
    }
}
