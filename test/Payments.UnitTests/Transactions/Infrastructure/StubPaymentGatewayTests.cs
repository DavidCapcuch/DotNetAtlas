using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Payments.Domain.Errors;
using Payments.Infrastructure.ExternalServices.PaymentGateway;
using Platform.SharedKernel.ValueObjects;

namespace Payments.UnitTests.Transactions.Infrastructure;

public class StubPaymentGatewayTests
{
    private readonly FakeTimeProvider _fakeTimeProvider = new(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero));
    private readonly StubPaymentGateway _sut;

    public StubPaymentGatewayTests()
    {
        _sut = new StubPaymentGateway(_fakeTimeProvider);
    }

    private DateTimeOffset UtcNow => _fakeTimeProvider.GetUtcNow();

    [Theory]
    [InlineData(9.99)]
    [InlineData(0.99)]
    [InlineData(99.99)]
    [InlineData(100.99)]
    [InlineData(1.99)]
    public async Task AuthorizeAsync_WhenAmountEndsIn99Cents_ReturnsGatewayDeclinedError(decimal amount)
    {
        var tx = PaymentTransactionFactory.Requested(UtcNow, amount: amount);

        var result = await _sut.AuthorizeAsync(tx, "stub-key", TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.HasError<GatewayDeclinedError>(e =>
                e.Reason == "Insufficient funds on file" && e.GatewayCode == "insufficient_funds")
                .Should().BeTrue();
        }
    }

    [Theory]
    [InlineData(1.00)]
    [InlineData(10.00)]
    [InlineData(99.00)]
    [InlineData(99.50)]
    [InlineData(100.00)]
    [InlineData(100.50)]
    [InlineData(0.01)]
    public async Task AuthorizeAsync_WhenAmountIsNormal_ReturnsSuccessWithDeterministicTransactionId(decimal amount)
    {
        var tx = PaymentTransactionFactory.Requested(UtcNow, amount: amount);
        var expectedId = $"stub-{tx.Id:N}";

        var result = await _sut.AuthorizeAsync(tx, "stub-key", TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.GatewayTransactionId.Should().Be(expectedId);
            result.Value.ResponseCode.Code.Should().Be("ok");
            result.Value.ResponseCode.Message.Should().Be("Approved");
        }
    }

    [Fact]
    public async Task AuthorizeAsync_PopulatesExpiresAtUtc_AsNowPlusSevenDays()
    {
        // H-6: ExpiresAtUtc lives on the adapter (real PSPs return their own value); the v1
        // stub reads from TimeProvider so the value is deterministic in tests.
        var tx = PaymentTransactionFactory.Requested(UtcNow, amount: 100m);

        var result = await _sut.AuthorizeAsync(tx, "stub-key", TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.ExpiresAtUtc.Should().Be(UtcNow.AddDays(7));
        }
    }

    [Fact]
    public async Task AuthorizeAsync_DeterministicId_IsStableAcrossCalls()
    {
        var tx = PaymentTransactionFactory.Requested(UtcNow, amount: 50m);

        var first = await _sut.AuthorizeAsync(tx, "stub-key", TestContext.Current.CancellationToken);
        var second = await _sut.AuthorizeAsync(tx, "stub-key", TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            first.Should().BeSuccess();
            second.Should().BeSuccess();
            second.Value.GatewayTransactionId.Should().Be(first.Value.GatewayTransactionId);
        }
    }

    [Fact]
    public async Task AuthorizeAsync_WithNullTransaction_ThrowsArgumentNullException()
    {
        var act = async () => await _sut.AuthorizeAsync(null!, "stub-key", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task AuthorizeAsync_WithBlankIdempotencyKey_ThrowsArgumentException(string? key)
    {
        // H-4: a real PSP adapter would reject a blank Idempotency-Key header; the stub mirrors
        // that behaviour so the contract is enforced uniformly across implementations.
        var tx = PaymentTransactionFactory.Requested(UtcNow, amount: 50m);

        var act = async () => await _sut.AuthorizeAsync(tx, key!, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CaptureAsync_AlwaysSucceedsAndEchoesGatewayTransactionId()
    {
        var amount = Money.Create(100m, "USD").Value;

        var result = await _sut.CaptureAsync("stub-abc", amount, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.GatewayTransactionId.Should().Be("stub-abc");
            result.Value.ResponseCode.Code.Should().Be("ok");
            result.Value.ResponseCode.Message.Should().Be("Captured");
        }
    }

    [Fact]
    public async Task CaptureAsync_DoesNotApplyTheAuthorizeDeclineRule()
    {
        // The .99 decline anchor is authorize-only; capture references an already-validated
        // transaction id, so it cannot replay the decline.
        var amount = Money.Create(9.99m, "USD").Value;

        var result = await _sut.CaptureAsync("stub-abc", amount, TestContext.Current.CancellationToken);

        result.Should().BeSuccess();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task CaptureAsync_WithBlankGatewayTransactionId_ThrowsArgumentException(string? gatewayTransactionId)
    {
        var amount = Money.Create(100m, "USD").Value;

        var act = async () => await _sut.CaptureAsync(gatewayTransactionId!, amount, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task VoidAsync_AlwaysSucceeds()
    {
        var result = await _sut.VoidAsync("stub-abc", TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.ResponseCode.Code.Should().Be("ok");
            result.Value.ResponseCode.Message.Should().Be("Voided");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task VoidAsync_WithBlankGatewayTransactionId_ThrowsArgumentException(string? gatewayTransactionId)
    {
        var act = async () => await _sut.VoidAsync(gatewayTransactionId!, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RefundAsync_AlwaysSucceeds()
    {
        var amount = Money.Create(100m, "USD").Value;

        var result = await _sut.RefundAsync("stub-abc", amount, "customer_cancelled", TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.ResponseCode.Code.Should().Be("ok");
            result.Value.ResponseCode.Message.Should().Be("Refunded");
        }
    }

    [Theory]
    [InlineData("", "reason")]
    [InlineData(" ", "reason")]
    [InlineData(null, "reason")]
    [InlineData("stub-abc", "")]
    [InlineData("stub-abc", " ")]
    [InlineData("stub-abc", null)]
    public async Task RefundAsync_WithBlankInputs_ThrowsArgumentException(string? gatewayTransactionId, string? reason)
    {
        var amount = Money.Create(100m, "USD").Value;

        var act = async () => await _sut.RefundAsync(
            gatewayTransactionId!, amount, reason!, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
