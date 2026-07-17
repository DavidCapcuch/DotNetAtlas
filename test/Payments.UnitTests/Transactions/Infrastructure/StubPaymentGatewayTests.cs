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

    // Representative magnitudes for the "cents == .99 declines" rule: sub-dollar, single-digit,
    // and over-100 kill the plausible mutations (== fixed literal, magnitude-gated guard).
    [Theory]
    [InlineData(0.99)]
    [InlineData(9.99)]
    [InlineData(100.99)]
    public async Task AuthorizeAsync_WhenAmountEndsIn99Cents_ReturnsGatewayDeclinedError(decimal amount)
    {
        // Arrange
        var tx = PaymentTransactionFactory.Requested(amount: amount);

        // Act
        var result = await _sut.AuthorizeAsync(tx, "stub-key", TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.HasError<GatewayDeclinedError>(e =>
                e.Reason == "Insufficient funds on file" && e.GatewayCode == "insufficient_funds")
                .Should().BeTrue();
        }
    }

    // Complement of the decline rule: min (0.01), near-miss cents (.50), and the 100 boundary
    // prove amounts NOT ending in .99 authorize cleanly.
    [Theory]
    [InlineData(0.01)]
    [InlineData(1.00)]
    [InlineData(99.50)]
    [InlineData(100.00)]
    [InlineData(100.50)]
    public async Task AuthorizeAsync_WhenAmountIsNormal_ReturnsSuccessWithDeterministicTransactionId(decimal amount)
    {
        // Arrange
        var tx = PaymentTransactionFactory.Requested(amount: amount);
        var expectedId = $"stub-{tx.Id:N}";

        // Act
        var result = await _sut.AuthorizeAsync(tx, "stub-key", TestContext.Current.CancellationToken);

        // Assert
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
        // Arrange
        // H-6: ExpiresAtUtc lives on the adapter (real PSPs return their own value); the v1
        // stub reads from TimeProvider so the value is deterministic in tests.
        var tx = PaymentTransactionFactory.Requested(amount: 100m);

        // Act
        var result = await _sut.AuthorizeAsync(tx, "stub-key", TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.ExpiresAtUtc.Should().Be(UtcNow.AddDays(7));
        }
    }

    [Fact]
    public async Task AuthorizeAsync_DeterministicId_IsStableAcrossCalls()
    {
        // Arrange
        var tx = PaymentTransactionFactory.Requested(amount: 50m);

        // Act
        var first = await _sut.AuthorizeAsync(tx, "stub-key", TestContext.Current.CancellationToken);
        var second = await _sut.AuthorizeAsync(tx, "stub-key", TestContext.Current.CancellationToken);

        // Assert
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
        // Arrange
        var act = async () => await _sut.AuthorizeAsync(null!, "stub-key", TestContext.Current.CancellationToken);

        // Act & Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task AuthorizeAsync_WithBlankIdempotencyKey_ThrowsArgumentException(string? key)
    {
        // Arrange
        // H-4: a real PSP adapter would reject a blank Idempotency-Key header; the stub mirrors
        // that behaviour so the contract is enforced uniformly across implementations.
        var tx = PaymentTransactionFactory.Requested(amount: 50m);
        var act = async () => await _sut.AuthorizeAsync(tx, key!, TestContext.Current.CancellationToken);

        // Act & Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CaptureAsync_AlwaysSucceedsAndEchoesGatewayTransactionId()
    {
        // Arrange
        var amount = Money.Create(100m, "USD").Value;

        // Act
        var result = await _sut.CaptureAsync("stub-abc", amount, TestContext.Current.CancellationToken);

        // Assert
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
        // Arrange
        // The .99 decline anchor is authorize-only; capture references an already-validated
        // transaction id, so it cannot replay the decline.
        var amount = Money.Create(9.99m, "USD").Value;

        // Act
        var result = await _sut.CaptureAsync("stub-abc", amount, TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeSuccess();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task CaptureAsync_WithBlankGatewayTransactionId_ThrowsArgumentException(string? gatewayTransactionId)
    {
        // Arrange
        var amount = Money.Create(100m, "USD").Value;
        var act = async () => await _sut.CaptureAsync(gatewayTransactionId!, amount, TestContext.Current.CancellationToken);

        // Act & Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task VoidAsync_AlwaysSucceeds()
    {
        // Arrange & Act
        var result = await _sut.VoidAsync("stub-abc", TestContext.Current.CancellationToken);

        // Assert
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
        // Arrange
        var act = async () => await _sut.VoidAsync(gatewayTransactionId!, TestContext.Current.CancellationToken);

        // Act & Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RefundAsync_AlwaysSucceeds()
    {
        // Arrange
        var amount = Money.Create(100m, "USD").Value;

        // Act
        var result = await _sut.RefundAsync("stub-abc", amount, "customer_cancelled", TestContext.Current.CancellationToken);

        // Assert
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
        // Arrange
        var amount = Money.Create(100m, "USD").Value;
        var act = async () => await _sut.RefundAsync(
            gatewayTransactionId!, amount, reason!, TestContext.Current.CancellationToken);

        // Act & Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
