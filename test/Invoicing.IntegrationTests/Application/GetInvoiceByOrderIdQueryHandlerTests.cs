using Invoicing.Application.Invoices.GetInvoiceById;
using Invoicing.Application.Invoices.GetInvoiceByOrderId;
using Invoicing.Infrastructure.Persistence.Database;
using Invoicing.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS;

namespace Invoicing.IntegrationTests.Application;

/// <summary>
/// Characterisation tests for <see cref="GetInvoiceByOrderIdQueryHandler"/> (issue #277).
/// Same response shape and authorization branches as
/// <see cref="GetInvoiceByIdQueryHandlerTests"/> — keyed off <c>OrderId</c> instead of
/// the invoice's own id. Must pass against both the pre- and post-#277 handler.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class GetInvoiceByOrderIdQueryHandlerTests
{
    private readonly IntegrationTestFixture _fixture;

    public GetInvoiceByOrderIdQueryHandlerTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.ResetOutboxSubstitute();
    }

    [Fact]
    public async Task Handle_returns_issued_invoice_keyed_by_OrderId_with_SAS_URL()
    {
        var ct = TestContext.Current.CancellationToken;
        // ADR-0015: snapshot wall-clock before the act so the BeCloseTo assertion can
        // tolerate the milliseconds between the handler's GetUtcNow() and ours.
        var nowSnapshot = DateTimeOffset.UtcNow;
        var (invoiceId, buyerId) = await _fixture.SeedIssuedInvoiceAsync(TimeProvider.System, ct);
        var orderId = await GetOrderIdAsync(invoiceId, ct);

        var result = await InvokeHandlerAsync(orderId, buyerId, isAdmin: false, ct);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.InvoiceId.Should().Be(invoiceId);
        dto.OrderId.Should().Be(orderId);
        dto.BuyerId.Should().Be(buyerId);
        dto.Status.Should().Be("Issued");
        dto.InvoiceNumber.Should().MatchRegex($@"^INV-{nowSnapshot.Year}-\d{{6}}$");
        dto.Lines.Should().NotBeEmpty();
        dto.PdfPresignedUrl.Should().NotBeNull();
        dto.PdfPresignedUrl!.ToString().Should().Contain(dto.InvoiceNumber!);
        dto.PdfPresignedUrlExpiresAtUtc.Should()
            .BeCloseTo(nowSnapshot.Add(TimeSpan.FromMinutes(10)), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Handle_returns_NotFound_when_buyer_requests_another_buyers_invoice()
    {
        var ct = TestContext.Current.CancellationToken;
        var (invoiceId, _) = await _fixture.SeedIssuedInvoiceAsync(TimeProvider.System, ct);
        var orderId = await GetOrderIdAsync(invoiceId, ct);
        var intruder = Guid.CreateVersion7();

        var result = await InvokeHandlerAsync(orderId, intruder, isAdmin: false, ct);

        result.IsFailed.Should().BeTrue();
        // The error message references OrderId (handler uses InvoiceForOrderNotFound).
        result.Errors.Should().ContainSingle(e => e.Message.Contains(orderId.ToString()));
    }

    [Fact]
    public async Task Handle_returns_invoice_for_admin_regardless_of_buyer()
    {
        var ct = TestContext.Current.CancellationToken;
        var (invoiceId, owner) = await _fixture.SeedIssuedInvoiceAsync(TimeProvider.System, ct);
        var orderId = await GetOrderIdAsync(invoiceId, ct);
        var admin = Guid.CreateVersion7();

        var result = await InvokeHandlerAsync(orderId, admin, isAdmin: true, ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.OrderId.Should().Be(orderId);
        result.Value.BuyerId.Should().Be(owner);
    }

    [Fact]
    public async Task Handle_returns_NotFound_when_no_invoice_exists_for_order()
    {
        var ct = TestContext.Current.CancellationToken;
        var missingOrder = Guid.CreateVersion7();

        var result = await InvokeHandlerAsync(missingOrder, Guid.CreateVersion7(), isAdmin: true, ct);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains(missingOrder.ToString()));
    }

    private async Task<Guid> GetOrderIdAsync(Guid invoiceId, CancellationToken ct)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        return await db.Invoices
            .AsNoTracking()
            .Where(i => i.Id == invoiceId)
            .Select(i => i.OrderId)
            .SingleAsync(ct);
    }

    private async Task<FluentResults.Result<GetInvoiceByIdResponse>> InvokeHandlerAsync(
        Guid orderId,
        Guid buyerId,
        bool isAdmin,
        CancellationToken ct)
    {
        await using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetInvoiceByOrderIdQuery, GetInvoiceByIdResponse>>();

        return await handler.HandleAsync(
            new GetInvoiceByOrderIdQuery { OrderId = orderId, BuyerId = buyerId, IsAdmin = isAdmin },
            ct);
    }
}
