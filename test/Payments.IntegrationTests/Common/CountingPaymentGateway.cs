using FluentResults;
using Payments.Application.Abstractions;
using Payments.Domain.Transactions;
using Platform.SharedKernel.ValueObjects;

namespace Payments.IntegrationTests.Common;

/// <summary>
/// Test-only spy decorator over <see cref="StubPaymentGateway"/> that exposes per-method call
/// counters. Used by Example 2.2 in <c>docs/bc-design/example-mapping/payments.md</c> to verify
/// the saga-retry short-circuit fires before the gateway is touched, and by Example 3.3 to
/// observe the bug-class void-post-capture path.
/// </summary>
/// <remarks>
/// Forwards every call to the wrapped <see cref="IPaymentGateway"/> so deterministic rules
/// (e.g. the <c>.99</c> decline) still fire — only the counts are observed. Counters are
/// <see cref="Interlocked"/>-incremented so the spy is safe under concurrent handler dispatch.
/// </remarks>
public sealed class CountingPaymentGateway : IPaymentGateway
{
    private readonly IPaymentGateway _inner;
    private int _authorizeCount;
    private int _captureCount;
    private int _voidCount;
    private int _refundCount;

    public CountingPaymentGateway(IPaymentGateway inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public int AuthorizeCount => Volatile.Read(ref _authorizeCount);

    public int CaptureCount => Volatile.Read(ref _captureCount);

    public int VoidCount => Volatile.Read(ref _voidCount);

    public int RefundCount => Volatile.Read(ref _refundCount);

    public void Reset()
    {
        Interlocked.Exchange(ref _authorizeCount, 0);
        Interlocked.Exchange(ref _captureCount, 0);
        Interlocked.Exchange(ref _voidCount, 0);
        Interlocked.Exchange(ref _refundCount, 0);
    }

    public Task<Result<AuthorizeResponse>> AuthorizeAsync(PaymentTransaction tx, CancellationToken ct)
    {
        Interlocked.Increment(ref _authorizeCount);
        return _inner.AuthorizeAsync(tx, ct);
    }

    public Task<Result<CaptureResponse>> CaptureAsync(string gatewayTransactionId, Money amount, CancellationToken ct)
    {
        Interlocked.Increment(ref _captureCount);
        return _inner.CaptureAsync(gatewayTransactionId, amount, ct);
    }

    public Task<Result<RefundResponse>> RefundAsync(string gatewayTransactionId, Money amount, string reason, CancellationToken ct)
    {
        Interlocked.Increment(ref _refundCount);
        return _inner.RefundAsync(gatewayTransactionId, amount, reason, ct);
    }

    public Task<Result<VoidResponse>> VoidAsync(string gatewayTransactionId, CancellationToken ct)
    {
        Interlocked.Increment(ref _voidCount);
        return _inner.VoidAsync(gatewayTransactionId, ct);
    }
}
