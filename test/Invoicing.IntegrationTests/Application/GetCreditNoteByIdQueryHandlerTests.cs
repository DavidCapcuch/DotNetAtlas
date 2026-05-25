using Invoicing.Application.CreditNotes.GetCreditNoteById;
using Invoicing.IntegrationTests.Common;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS;

namespace Invoicing.IntegrationTests.Application;

/// <summary>
/// Characterisation tests for <see cref="GetCreditNoteByIdQueryHandler"/> (issue #277).
/// Pins the credit-note projection shape, per-request SAS URL minting, and authorization
/// branches. Must pass against both the pre- and post-#277 handler.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class GetCreditNoteByIdQueryHandlerTests
{
    private readonly IntegrationTestFixture _fixture;

    public GetCreditNoteByIdQueryHandlerTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.ResetOutboxSubstitute();
    }

    [Fact]
    public async Task Handle_returns_issued_credit_note_with_lines_and_freshly_minted_SAS_URL()
    {
        var ct = TestContext.Current.CancellationToken;
        var (creditNoteId, buyerId) = await _fixture.SeedIssuedCreditNoteAsync(ct);

        var result = await InvokeHandlerAsync(creditNoteId, buyerId, isAdmin: false, ct);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.CreditNoteId.Should().Be(creditNoteId);
        dto.BuyerId.Should().Be(buyerId);
        dto.Status.Should().Be("Issued");
        dto.CreditNoteNumber.Should().MatchRegex(@"^CN-2026-\d{6}$");
        dto.OriginalInvoiceNumber.Should().MatchRegex(@"^INV-2026-\d{6}$");
        dto.Reason.Should().NotBeNullOrEmpty();
        dto.Currency.Should().Be("EUR");
        // I-CN-2: credit-note totals are negative.
        dto.TotalAmount.Should().BeLessThan(0m);
        dto.Lines.Should().NotBeEmpty();
        dto.DeliveredAtUtc.Should().BeNull();
        dto.PdfPresignedUrl.Should().NotBeNull();
        dto.PdfPresignedUrl!.ToString().Should().Contain(dto.CreditNoteNumber);
        dto.PdfPresignedUrlExpiresAtUtc.Should()
            .Be(IntegrationTestFixture.FixedFakeNow.Add(TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public async Task Handle_returns_NotFound_when_buyer_requests_another_buyers_credit_note()
    {
        var ct = TestContext.Current.CancellationToken;
        var (creditNoteId, _) = await _fixture.SeedIssuedCreditNoteAsync(ct);
        var intruder = Guid.CreateVersion7();

        var result = await InvokeHandlerAsync(creditNoteId, intruder, isAdmin: false, ct);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains(creditNoteId.ToString()));
    }

    [Fact]
    public async Task Handle_returns_credit_note_for_admin_regardless_of_buyer()
    {
        var ct = TestContext.Current.CancellationToken;
        var (creditNoteId, owner) = await _fixture.SeedIssuedCreditNoteAsync(ct);
        var admin = Guid.CreateVersion7();

        var result = await InvokeHandlerAsync(creditNoteId, admin, isAdmin: true, ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.CreditNoteId.Should().Be(creditNoteId);
        result.Value.BuyerId.Should().Be(owner);
    }

    [Fact]
    public async Task Handle_returns_NotFound_when_credit_note_does_not_exist()
    {
        var ct = TestContext.Current.CancellationToken;
        var missing = Guid.CreateVersion7();

        var result = await InvokeHandlerAsync(missing, Guid.CreateVersion7(), isAdmin: true, ct);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains(missing.ToString()));
    }

    private async Task<FluentResults.Result<GetCreditNoteByIdResponse>> InvokeHandlerAsync(
        Guid creditNoteId,
        Guid buyerId,
        bool isAdmin,
        CancellationToken ct)
    {
        await using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetCreditNoteByIdQuery, GetCreditNoteByIdResponse>>();

        return await handler.HandleAsync(
            new GetCreditNoteByIdQuery { CreditNoteId = creditNoteId, BuyerId = buyerId, IsAdmin = isAdmin },
            ct);
    }
}
