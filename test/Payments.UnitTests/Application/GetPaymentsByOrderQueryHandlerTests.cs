using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Payments.Application.Common.Data;
using Payments.Application.Transactions.GetPaymentsByOrder;
using Payments.Domain.Transactions;
using Payments.UnitTests.Transactions;

namespace Payments.UnitTests.Application;

public class GetPaymentsByOrderQueryHandlerTests
{
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero));
    private readonly IPaymentRepository _repository = Substitute.For<IPaymentRepository>();

    [Fact]
    public async Task Handle_OrderWithPayments_ReturnsList()
    {
        var orderId = Guid.CreateVersion7();
        var p1 = PaymentTransactionFactory.Authorized(_timeProvider.GetUtcNow());
        var p2 = PaymentTransactionFactory.Failed(_timeProvider.GetUtcNow());
        _repository.GetByOrderIdAsync(orderId, Arg.Any<CancellationToken>())
            .Returns(new List<PaymentTransaction> { p1, p2 });
        var handler = new GetPaymentsByOrderQueryHandler(_repository);

        var result = await handler.HandleAsync(new GetPaymentsByOrderQuery(orderId), TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.OrderId.Should().Be(orderId);
            result.Value.Payments.Should().HaveCount(2);
        }
    }

    [Fact]
    public async Task Handle_OrderWithNoPayments_ReturnsEmptyList()
    {
        var orderId = Guid.CreateVersion7();
        _repository.GetByOrderIdAsync(orderId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<PaymentTransaction>());
        var handler = new GetPaymentsByOrderQueryHandler(_repository);

        var result = await handler.HandleAsync(new GetPaymentsByOrderQuery(orderId), TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Payments.Should().BeEmpty();
        }
    }
}
