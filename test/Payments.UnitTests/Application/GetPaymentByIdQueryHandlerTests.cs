using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Payments.Application.Common.Data;
using Payments.Application.Transactions.GetPaymentById;
using Payments.Domain.Transactions;
using Payments.UnitTests.Transactions;

namespace Payments.UnitTests.Application;

public class GetPaymentByIdQueryHandlerTests
{
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero));
    private readonly IPaymentRepository _repository = Substitute.For<IPaymentRepository>();

    [Fact]
    public async Task Handle_ExistingPayment_ReturnsResponse()
    {
        var existing = PaymentTransactionFactory.Authorized(_timeProvider.GetUtcNow());
        _repository.GetByIdAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(existing);
        var handler = new GetPaymentByIdQueryHandler(_repository);

        var result = await handler.HandleAsync(new GetPaymentByIdQuery(existing.Id), TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.PaymentId.Should().Be(existing.Id);
            result.Value.Status.Should().Be("Authorized");
            // ADR-0011 — response masks sensitive tokens to last-4 (see PaymentTransactionResponseMapper.MaskTrailing).
            // Default seed is "gw-tx-abc123" → "****c123".
            result.Value.GatewayTransactionId.Should().Be("****c123");
            result.Value.AuthorizedAtUtc.Should().Be(_timeProvider.GetUtcNow());
        }
    }

    [Fact]
    public async Task Handle_MissingPayment_ReturnsNotFound()
    {
        var paymentId = Guid.CreateVersion7();
        _repository.GetByIdAsync(paymentId, Arg.Any<CancellationToken>()).Returns((PaymentTransaction?)null);
        var handler = new GetPaymentByIdQueryHandler(_repository);

        var result = await handler.HandleAsync(new GetPaymentByIdQuery(paymentId), TestContext.Current.CancellationToken);

        result.Should().BeFailure();
    }

    [Fact]
    public async Task Handle_FailedPayment_IncludesFailureInfo()
    {
        var existing = PaymentTransactionFactory.Failed(_timeProvider.GetUtcNow());
        _repository.GetByIdAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(existing);
        var handler = new GetPaymentByIdQueryHandler(_repository);

        var result = await handler.HandleAsync(new GetPaymentByIdQuery(existing.Id), TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.FailureInfo.Should().NotBeNull();
            result.Value.FailureInfo!.Reason.Should().Be("InsufficientFunds");
            result.Value.FailureInfo.GatewayCode.Should().Be("insufficient_funds");
        }
    }
}
