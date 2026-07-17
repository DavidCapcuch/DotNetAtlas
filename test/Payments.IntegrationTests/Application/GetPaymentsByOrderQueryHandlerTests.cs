using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Payments.Application.Transactions.GetPaymentsByOrder;
using Payments.Domain.Transactions;
using Payments.Infrastructure.Persistence.Database;
using Payments.IntegrationTests.Common;
using Platform.CQRS;
using Platform.SharedKernel.ValueObjects;

namespace Payments.IntegrationTests.Application;

/// <summary>
/// Characterisation tests for <see cref="GetPaymentsByOrderQueryHandler"/>'s admin list
/// projection against real Postgres. Integration-tier for the same reason as
/// <see cref="GetPaymentByIdQueryHandlerTests"/> — the read handler is inline LINQ (ADR-0022)
/// and materialises owned value objects the InMemory provider does not round-trip.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class GetPaymentsByOrderQueryHandlerTests
{
    private readonly IntegrationTestFixture _fixture;

    public GetPaymentsByOrderQueryHandlerTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Handle_OrderWithPayment_ReturnsSingletonList()
    {
        // Arrange
        // ADR-0029: one payment per order (unique ux_payment_transactions_order_id), so the
        // by-order projection returns at most one row.
        var ct = TestContext.Current.CancellationToken;
        var orderId = Guid.CreateVersion7();
        await SeedPaymentForOrderAsync(orderId, ct);

        using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetPaymentsByOrderQuery, GetPaymentsByOrderResponse>>();

        // Act
        var result = await handler.HandleAsync(new GetPaymentsByOrderQuery(orderId), ct);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.OrderId.Should().Be(orderId);
            result.Value.Payments.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task Handle_OrderWithNoPayments_ReturnsEmptyList()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var orderId = Guid.CreateVersion7();

        using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetPaymentsByOrderQuery, GetPaymentsByOrderResponse>>();

        // Act
        var result = await handler.HandleAsync(new GetPaymentsByOrderQuery(orderId), ct);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Payments.Should().BeEmpty();
        }
    }

    private async Task SeedPaymentForOrderAsync(Guid orderId, CancellationToken ct)
    {
        using var scope = _fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();

        var tx = PaymentTransaction.Create(
            Guid.CreateVersion7(),
            buyerId: Guid.CreateVersion7(),
            orderId: orderId,
            Money.Create(100m, "USD").Value,
            paymentMethodId: "tok_visa_4242").Value;
        _ = tx.PopDomainEvents();

        dbContext.Transactions.Add(tx);
        await dbContext.SaveChangesAsync(ct);
    }
}
