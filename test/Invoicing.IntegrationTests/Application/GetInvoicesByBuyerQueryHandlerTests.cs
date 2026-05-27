using Invoicing.Application.Invoices.GetInvoicesByBuyer;
using Invoicing.IntegrationTests.Common;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS;

namespace Invoicing.IntegrationTests.Application;

/// <summary>
/// Characterisation tests for <see cref="GetInvoicesByBuyerQueryHandler"/> (issue #277).
/// Pins the paged-list projection shape and per-row SAS URL minting against both the
/// pre- and post-#277 handler.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class GetInvoicesByBuyerQueryHandlerTests
{
    private readonly IntegrationTestFixture _fixture;

    public GetInvoicesByBuyerQueryHandlerTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.ResetOutboxSubstitute();
    }

    [Fact]
    public async Task Handle_returns_single_issued_invoice_for_buyer_with_freshly_minted_SAS_URL()
    {
        var ct = TestContext.Current.CancellationToken;
        // ADR-0015: snapshot wall-clock before the act for the BeCloseTo assertion below.
        var nowSnapshot = DateTimeOffset.UtcNow;
        var (invoiceId, buyerId) = await _fixture.SeedIssuedInvoiceAsync(TimeProvider.System, ct);

        var result = await InvokeHandlerAsync(buyerId, pageNumber: 1, pageSize: 20, ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.PageNumber.Should().Be(1);
        result.Value.PageSize.Should().Be(20);
        result.Value.Total.Should().Be(1);
        result.Value.Items.Should().ContainSingle();
        var dto = result.Value.Items[0];
        dto.InvoiceId.Should().Be(invoiceId);
        dto.BuyerId.Should().Be(buyerId);
        dto.Status.Should().Be("Issued");
        dto.InvoiceNumber.Should().MatchRegex($@"^INV-{nowSnapshot.Year}-\d{{6}}$");
        dto.PdfPresignedUrl.Should().NotBeNull();
        dto.PdfPresignedUrl!.ToString().Should().Contain(dto.InvoiceNumber!);
        dto.PdfPresignedUrlExpiresAtUtc.Should()
            .BeCloseTo(nowSnapshot.Add(TimeSpan.FromMinutes(10)), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Handle_returns_empty_envelope_when_buyer_has_no_invoices()
    {
        var ct = TestContext.Current.CancellationToken;
        var buyerWithNoInvoices = Guid.CreateVersion7();

        var result = await InvokeHandlerAsync(buyerWithNoInvoices, pageNumber: 1, pageSize: 20, ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
        result.Value.Total.Should().Be(0);
        result.Value.PageNumber.Should().Be(1);
        result.Value.PageSize.Should().Be(20);
    }

    [Fact]
    public async Task Handle_excludes_other_buyers_invoices()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, ownerA) = await _fixture.SeedIssuedInvoiceAsync(TimeProvider.System, ct);
        await _fixture.SeedIssuedInvoiceAsync(TimeProvider.System, ct); // owned by a different (random) buyer

        var result = await InvokeHandlerAsync(ownerA, pageNumber: 1, pageSize: 20, ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.Total.Should().Be(1);
        result.Value.Items.Should().ContainSingle();
        result.Value.Items[0].BuyerId.Should().Be(ownerA);
    }

    [Fact]
    public async Task Handle_honours_pageNumber_and_pageSize_for_paging()
    {
        var ct = TestContext.Current.CancellationToken;
        var emptyResult = await InvokeHandlerAsync(Guid.CreateVersion7(), pageNumber: 20, pageSize: 5, ct);

        emptyResult.IsSuccess.Should().BeTrue();
        emptyResult.Value.PageNumber.Should().Be(20);
        emptyResult.Value.PageSize.Should().Be(5);
        emptyResult.Value.Total.Should().Be(0);
        emptyResult.Value.Items.Should().BeEmpty();
    }

    private async Task<FluentResults.Result<GetInvoicesByBuyerResponse>> InvokeHandlerAsync(
        Guid buyerId,
        int pageNumber,
        int pageSize,
        CancellationToken ct)
    {
        await using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetInvoicesByBuyerQuery, GetInvoicesByBuyerResponse>>();

        return await handler.HandleAsync(
            new GetInvoicesByBuyerQuery { BuyerId = buyerId, PageNumber = pageNumber, PageSize = pageSize },
            ct);
    }
}
