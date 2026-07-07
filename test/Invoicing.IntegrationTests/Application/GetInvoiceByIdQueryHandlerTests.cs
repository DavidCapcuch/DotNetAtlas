using Invoicing.Application.Invoices.GetInvoiceById;
using Invoicing.IntegrationTests.Common;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS;

namespace Invoicing.IntegrationTests.Application;

/// <summary>
/// Characterisation tests for <see cref="GetInvoiceByIdQueryHandler"/>'s projection of an
/// issued invoice, its per-request SAS URL minting, and authorization branches (issue #277).
/// The same assertions must pass against both the legacy
/// <c>WithSpecification(InvoiceByIdSpec)</c> handler AND the SQL-side-projection rewrite
/// that drops Ardalis.Specification on the read side.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class GetInvoiceByIdQueryHandlerTests
{
    private readonly IntegrationTestFixture _fixture;

    public GetInvoiceByIdQueryHandlerTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.ResetOutboxSubstitute();
    }

    [Fact]
    public async Task Handle_returns_issued_invoice_with_lines_VAT_address_and_freshly_minted_SAS_URL()
    {
        var ct = TestContext.Current.CancellationToken;
        // ADR-0015: Generic Host registers TimeProvider.System; the SAS-URL minting and
        // invoice-number year both come from wall-clock. Snapshot before the act so the
        // BeCloseTo assertion below can tolerate the few milliseconds between the
        // handler's GetUtcNow() and ours.
        var nowSnapshot = DateTimeOffset.UtcNow;
        var (invoiceId, buyerId) = await _fixture.SeedIssuedInvoiceAsync(TimeProvider.System, ct);

        var result = await InvokeHandlerAsync(invoiceId, buyerId, isAdmin: false, ct);

        using (new AssertionScope())
        {
            result.IsSuccess.Should().BeTrue();
            var dto = result.Value;
            dto.InvoiceId.Should().Be(invoiceId);
            dto.BuyerId.Should().Be(buyerId);
            dto.Status.Should().Be("Issued");
            dto.InvoiceNumber.Should().MatchRegex($@"^INV-{nowSnapshot.Year}-\d{{6}}$");
            dto.Currency.Should().Be("EUR");
            dto.TotalAmount.Should().BeGreaterThan(0m);
            dto.Lines.Should().NotBeEmpty();
            // The seed produces a zero-VAT line, so the projected VatLines collection comes back empty —
            // we just pin that the materialised collection is non-null (i.e., EF translates the
            // owned-collection .Select(...) — Lines covers the not-empty side of that proof).
            dto.VatLines.Should().NotBeNull();
            dto.BillingAddress.City.Should().Be("Prague");
            dto.BillingAddress.PostalCode.Should().Be("11000");
            dto.Cancellation.Should().BeNull();
            dto.DeliveredAtUtc.Should().BeNull();
            dto.PdfPresignedUrl.Should().NotBeNull();
            dto.PdfPresignedUrl!.ToString().Should().Contain(dto.InvoiceNumber!);
            dto.PdfPresignedUrlExpiresAtUtc.Should()
                .BeCloseTo(nowSnapshot.Add(TimeSpan.FromMinutes(10)), TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Handle_returns_delivered_invoice_with_DeliveredAtUtc_populated()
    {
        var ct = TestContext.Current.CancellationToken;
        var (invoiceId, buyerId) = await _fixture.SeedDeliveredInvoiceAsync(TimeProvider.System, ct);

        var result = await InvokeHandlerAsync(invoiceId, buyerId, isAdmin: false, ct);

        using (new AssertionScope())
        {
            result.IsSuccess.Should().BeTrue();
            result.Value.Status.Should().Be("Delivered");
            result.Value.DeliveredAtUtc.Should().NotBeNull();
            result.Value.PdfPresignedUrl.Should().NotBeNull();
        }
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task Handle_returns_NotFound_when_buyer_requests_another_buyers_invoice()
    {
        var ct = TestContext.Current.CancellationToken;
        var (invoiceId, _) = await _fixture.SeedIssuedInvoiceAsync(TimeProvider.System, ct);
        var intruder = Guid.CreateVersion7();

        var result = await InvokeHandlerAsync(invoiceId, intruder, isAdmin: false, ct);

        using (new AssertionScope())
        {
            result.IsFailed.Should().BeTrue();
            result.Errors.Should().ContainSingle(e => e.Message.Contains(invoiceId.ToString()));
        }
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task Handle_returns_invoice_for_admin_regardless_of_buyer()
    {
        var ct = TestContext.Current.CancellationToken;
        var (invoiceId, owner) = await _fixture.SeedIssuedInvoiceAsync(TimeProvider.System, ct);
        var admin = Guid.CreateVersion7();

        var result = await InvokeHandlerAsync(invoiceId, admin, isAdmin: true, ct);

        using (new AssertionScope())
        {
            result.IsSuccess.Should().BeTrue();
            result.Value.InvoiceId.Should().Be(invoiceId);
            result.Value.BuyerId.Should().Be(owner);
        }
    }

    [Fact]
    public async Task Handle_returns_NotFound_when_invoice_does_not_exist()
    {
        var ct = TestContext.Current.CancellationToken;
        var missing = Guid.CreateVersion7();

        var result = await InvokeHandlerAsync(missing, Guid.CreateVersion7(), isAdmin: true, ct);

        using (new AssertionScope())
        {
            result.IsFailed.Should().BeTrue();
            result.Errors.Should().ContainSingle(e => e.Message.Contains(missing.ToString()));
        }
    }

    private async Task<FluentResults.Result<GetInvoiceByIdResponse>> InvokeHandlerAsync(
        Guid invoiceId,
        Guid buyerId,
        bool isAdmin,
        CancellationToken ct)
    {
        await using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetInvoiceByIdQuery, GetInvoiceByIdResponse>>();

        return await handler.HandleAsync(
            new GetInvoiceByIdQuery { InvoiceId = invoiceId, BuyerId = buyerId, IsAdmin = isAdmin },
            ct);
    }
}
