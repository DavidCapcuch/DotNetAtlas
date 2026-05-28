using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payments.Application.Common.Data;
using Payments.Domain.Transactions;
using Payments.Infrastructure.Persistence.Database;
using Payments.IntegrationTests.Common;
using Platform.SharedKernel.ValueObjects;

namespace Payments.IntegrationTests.Infrastructure;

/// <summary>
/// Smoke tests for the <see cref="PaymentRepository"/> tracking split (#251).
/// Verifies that <see cref="IPaymentRepository.GetByIdForUpdateAsync"/> attaches the aggregate
/// to the DbContext change tracker (write-side handlers depend on this for mutation flushes),
/// while <see cref="IPaymentRepository.GetByIdAsNoTrackingAsync"/> returns the entity detached
/// (read-side query handler depends on this to keep the change tracker cold).
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class PaymentRepositoryIntegrationTests
{
    private readonly IntegrationTestFixture _fixture;

    public PaymentRepositoryIntegrationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetByIdAsNoTrackingAsync_ReturnsDetachedEntity()
    {
        var paymentId = await SeedPaymentAsync();

        using var scope = _fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();

        var tx = await repository.GetByIdAsNoTrackingAsync(paymentId, TestContext.Current.CancellationToken);

        tx.Should().NotBeNull();
        dbContext.ChangeTracker.Entries<PaymentTransaction>().Should().BeEmpty(
            "AsNoTracking variant must return a detached entity so the read-side query handler " +
            "cannot leak accidental mutations into a downstream SaveChangesAsync (#251).");
    }

    [Fact]
    public async Task GetByIdForUpdateAsync_ReturnsTrackedEntity()
    {
        var paymentId = await SeedPaymentAsync();

        using var scope = _fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();

        var tx = await repository.GetByIdForUpdateAsync(paymentId, TestContext.Current.CancellationToken);

        tx.Should().NotBeNull();
        var entry = dbContext.Entry(tx!);
        entry.State.Should().Be(
            EntityState.Unchanged,
            "ForUpdate variant must return a tracked entity so command handlers can mutate the " +
            "aggregate and flush via SaveChangesAsync (#251).");
    }

    private async Task<Guid> SeedPaymentAsync()
    {
        var paymentId = Guid.CreateVersion7();
        var correlationId = Guid.CreateVersion7();
        var amount = Money.Create(100m, "USD").Value;

        using var scope = _fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();

        var tx = PaymentTransaction.Create(
            paymentId,
            correlationId,
            buyerId: Guid.CreateVersion7(),
            orderId: Guid.CreateVersion7(),
            amount,
            "tok_visa_4242",
            DateTimeOffset.UtcNow).Value;

        repository.Add(tx);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return paymentId;
    }
}
