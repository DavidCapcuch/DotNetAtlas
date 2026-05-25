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
[Collection(nameof(IntegrationTestCollection))]
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
        var (invoiceId, buyerId) = await _fixture.SeedIssuedInvoiceAsync(ct);

        var result = await InvokeHandlerAsync(buyerId, skip: 0, take: 20, ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.Skip.Should().Be(0);
        result.Value.Take.Should().Be(20);
        result.Value.Invoices.Should().ContainSingle();
        var dto = result.Value.Invoices[0];
        dto.InvoiceId.Should().Be(invoiceId);
        dto.BuyerId.Should().Be(buyerId);
        dto.Status.Should().Be("Issued");
        dto.InvoiceNumber.Should().MatchRegex(@"^INV-2026-\d{6}$");
        dto.PdfPresignedUrl.Should().NotBeNull();
        dto.PdfPresignedUrl!.ToString().Should().Contain(dto.InvoiceNumber!);
        dto.PdfPresignedUrlExpiresAtUtc.Should()
            .Be(IntegrationTestFixture.FixedFakeNow.Add(TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public async Task Handle_returns_empty_envelope_when_buyer_has_no_invoices()
    {
        var ct = TestContext.Current.CancellationToken;
        var buyerWithNoInvoices = Guid.CreateVersion7();

        var result = await InvokeHandlerAsync(buyerWithNoInvoices, skip: 0, take: 20, ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.Invoices.Should().BeEmpty();
        result.Value.Skip.Should().Be(0);
        result.Value.Take.Should().Be(20);
    }

    [Fact]
    public async Task Handle_excludes_other_buyers_invoices()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, ownerA) = await _fixture.SeedIssuedInvoiceAsync(ct);
        await _fixture.SeedIssuedInvoiceAsync(ct); // owned by a different (random) buyer

        var result = await InvokeHandlerAsync(ownerA, skip: 0, take: 20, ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.Invoices.Should().ContainSingle();
        result.Value.Invoices[0].BuyerId.Should().Be(ownerA);
    }

    [Fact]
    public async Task Handle_honours_skip_and_take_for_paging()
    {
        var ct = TestContext.Current.CancellationToken;
        var emptyResult = await InvokeHandlerAsync(Guid.CreateVersion7(), skip: 99, take: 5, ct);

        emptyResult.IsSuccess.Should().BeTrue();
        emptyResult.Value.Skip.Should().Be(99);
        emptyResult.Value.Take.Should().Be(5);
        emptyResult.Value.Invoices.Should().BeEmpty();
    }

    private async Task<FluentResults.Result<GetInvoicesByBuyerResponse>> InvokeHandlerAsync(
        Guid buyerId,
        int skip,
        int take,
        CancellationToken ct)
    {
        await using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetInvoicesByBuyerQuery, GetInvoicesByBuyerResponse>>();

        return await handler.HandleAsync(
            new GetInvoicesByBuyerQuery { BuyerId = buyerId, Skip = skip, Take = take },
            ct);
    }
}
