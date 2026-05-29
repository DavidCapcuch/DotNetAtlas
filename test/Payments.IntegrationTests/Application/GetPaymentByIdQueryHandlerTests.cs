using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Payments.Application.Transactions.GetPaymentById;
using Payments.Domain.Transactions;
using Payments.Domain.Transactions.ValueObjects;
using Payments.Infrastructure.Persistence.Database;
using Payments.IntegrationTests.Common;
using Platform.CQRS;
using Platform.SharedKernel.ValueObjects;

namespace Payments.IntegrationTests.Application;

/// <summary>
/// Characterisation tests for <see cref="GetPaymentByIdQueryHandler"/>'s projection (Status,
/// ADR-0011 PII-token masking, FailureInfo) against real Postgres.
/// </summary>
/// <remarks>
/// Lives at the integration tier (not the InMemory unit tier) because the handler's
/// <c>AsNoTracking</c> projection materialises owned value objects / SmartEnums that the EF
/// InMemory provider does not round-trip — see <c>TestPaymentsDbContext</c>. Per ADR-0022 the
/// read side is inline LINQ (no spec), exercised end-to-end here.
/// </remarks>
[Collection<IntegrationTestCollection>]
public sealed class GetPaymentByIdQueryHandlerTests
{
    private readonly IntegrationTestFixture _fixture;

    public GetPaymentByIdQueryHandlerTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Handle_ExistingAuthorizedPayment_ReturnsMaskedResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        var utcNow = DateTimeOffset.UtcNow;
        var paymentId = await SeedAuthorizedAsync(utcNow, ct);

        using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetPaymentByIdQuery, GetPaymentByIdResponse>>();

        var result = await handler.HandleAsync(new GetPaymentByIdQuery(paymentId), ct);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.PaymentId.Should().Be(paymentId);
            result.Value.Status.Should().Be("Authorized");
            // ADR-0011 — response masks sensitive tokens to last-4. Seed "gw-tx-abc123" -> "****c123".
            result.Value.GatewayTransactionId.Should().Be("****c123");
            // Postgres timestamptz truncates 100-ns precision to microseconds.
            result.Value.AuthorizedAtUtc.Should().BeCloseTo(utcNow, TimeSpan.FromSeconds(1));
        }
    }

    [Fact]
    public async Task Handle_MissingPayment_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;

        using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetPaymentByIdQuery, GetPaymentByIdResponse>>();

        var result = await handler.HandleAsync(new GetPaymentByIdQuery(Guid.CreateVersion7()), ct);

        result.Should().BeFailure();
    }

    [Fact]
    public async Task Handle_FailedPayment_IncludesFailureInfo()
    {
        var ct = TestContext.Current.CancellationToken;
        var paymentId = await SeedFailedAsync(DateTimeOffset.UtcNow, ct);

        using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetPaymentByIdQuery, GetPaymentByIdResponse>>();

        var result = await handler.HandleAsync(new GetPaymentByIdQuery(paymentId), ct);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.FailureInfo.Should().NotBeNull();
            result.Value.FailureInfo!.Reason.Should().Be("InsufficientFunds");
            result.Value.FailureInfo.GatewayCode.Should().Be("insufficient_funds");
        }
    }

    private async Task<Guid> SeedAuthorizedAsync(DateTimeOffset utcNow, CancellationToken ct)
    {
        using var scope = _fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();

        var tx = PaymentTransaction.Create(
            Guid.CreateVersion7(),
            correlationId: Guid.CreateVersion7(),
            buyerId: Guid.CreateVersion7(),
            orderId: Guid.CreateVersion7(),
            Money.Create(100m, "USD").Value,
            paymentMethodId: "tok_visa_4242",
            utcNow).Value;
        _ = tx.PopDomainEvents();
        tx.Authorize("gw-tx-abc123", GatewayResponseCode.Create("ok", "Approved"), utcNow.AddDays(7), utcNow);
        _ = tx.PopDomainEvents();

        dbContext.Transactions.Add(tx);
        await dbContext.SaveChangesAsync(ct);
        return tx.Id;
    }

    private async Task<Guid> SeedFailedAsync(DateTimeOffset utcNow, CancellationToken ct)
    {
        using var scope = _fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();

        var tx = PaymentTransaction.Create(
            Guid.CreateVersion7(),
            correlationId: Guid.CreateVersion7(),
            buyerId: Guid.CreateVersion7(),
            orderId: Guid.CreateVersion7(),
            Money.Create(100m, "USD").Value,
            paymentMethodId: "tok_visa_4242",
            utcNow).Value;
        _ = tx.PopDomainEvents();
        tx.MarkAuthorizationFailed(
            FailureInfo.Create(FailureReason.InsufficientFunds, "insufficient_funds", utcNow),
            utcNow);
        _ = tx.PopDomainEvents();

        dbContext.Transactions.Add(tx);
        await dbContext.SaveChangesAsync(ct);
        return tx.Id;
    }
}
