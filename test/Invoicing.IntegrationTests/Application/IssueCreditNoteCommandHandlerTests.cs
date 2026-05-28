using System.Text.Json;
using AwesomeAssertions;
using Invoicing.Application.Common.Data;
using Invoicing.Application.CreditNotes.IssueCreditNote;
using Invoicing.Application.CreditNotes.Projections;
using Invoicing.Application.Invoices.IssueInvoice;
using Invoicing.Application.Invoices.Projections;
using Invoicing.Domain.CreditNotes.ValueObjects;
using Invoicing.Domain.Invoices;
using Invoicing.Domain.Invoices.ValueObjects;
using Invoicing.Infrastructure.Persistence.Database;
using Invoicing.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Platform.CQRS;
using Platform.ReliableMessaging.Outbox.EFCore;
using Xunit;

namespace Invoicing.IntegrationTests.Application;

/// <summary>
/// Integration tests for <c>IssueCreditNoteCommandHandler</c>. Each test seeds a
/// pre-issued <see cref="Invoice"/> via the invoice handler (so the prerequisite
/// state is built through real production code), then drives the credit-note flow.
/// Covers example-mapping § 3.1 (happy path) and § 3.3 (cancelled-invoice rejected).
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class IssueCreditNoteCommandHandlerTests
{
    private readonly IntegrationTestFixture _fixture;

    public IssueCreditNoteCommandHandlerTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.ResetOutboxSubstitute();
    }

    [Fact]
    public async Task Example_3_1_HappyPath_IssuesCreditNote_CancelsOriginal_AndEmitsBothEvents()
    {
        var ct = TestContext.Current.CancellationToken;
        var orderId = Guid.CreateVersion7();
        var paymentId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();
        // ADR-0015: snapshot wall-clock before the act so the BeCloseTo IssueDate
        // assertion + dynamic-year regex line up with what the handler observed.
        var nowSnapshot = DateTimeOffset.UtcNow;
        // Single saga correlation id threads through Order → Invoice → cancellation → CreditNote.
        // OrderCancelledEvent / PaymentRefundedEvent reuse the original Checkout saga correlation
        // per events-catalog § 5.3, so Invoice.CorrelationId equals the cancellation flow's
        // pending_credit_notes.CorrelationId. The InvoiceCancelledEvent reflects this — its
        // CorrelationId comes from the loaded Invoice's CorrelationId, identical to the credit
        // note's correlation.
        var sagaCorrelationId = Guid.CreateVersion7();
        const decimal Total = 152.00m;
        const string Currency = "EUR";

        // Seed a real Invoice via the M7 handler so the credit-note path operates
        // against production-shape state.
        var invoiceId = await IssueInvoiceAsync(
            sagaCorrelationId, orderId, paymentId, buyerId, Total, Currency, ct);

        _fixture.ResetOutboxSubstitute();

        await SeedConvergedPendingCreditNoteAsync(
            sagaCorrelationId, orderId, paymentId, buyerId,
            refundedAmount: Total, currency: Currency, ct);

        await using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<IssueCreditNoteCommand, Guid>>();

        var result = await handler.HandleAsync(
            new IssueCreditNoteCommand { CorrelationId = sagaCorrelationId }, ct);

        using var _ = new AssertionScope();
        result.IsSuccess.Should().BeTrue();
        var creditNoteId = result.Value;

        await using var assertScope = _fixture.CreateScope();
        var db = assertScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();

        // Credit note persisted in Issued status with negative total.
        var creditNote = await db.CreditNotes
            .Include(cn => cn.Lines)
            .AsNoTracking()
            .SingleAsync(cn => cn.Id == creditNoteId, ct);
        creditNote.Status.Should().Be(CreditNoteStatus.Issued);
        creditNote.CreditNoteNumber.Should().NotBeNull();
        creditNote.CreditNoteNumber!.Value.Should().MatchRegex($@"^CN-{nowSnapshot.Year}-\d{{6}}$");
        creditNote.OriginalInvoiceId.Should().Be(invoiceId);
        creditNote.Total.Amount.Should().Be(-Total, "credit note totals are negative (I-CN-2)");
        creditNote.Total.Currency.Name.Should().Be(Currency);
        creditNote.PdfBlobRef.Should().NotBeNull();
        creditNote.IssueDate.Should().BeCloseTo(nowSnapshot, TimeSpan.FromSeconds(5));
        creditNote.Lines.Should().NotBeEmpty();

        // Original invoice transitioned to Cancelled with the cancellation info.
        var originalInvoice = await db.Invoices.AsNoTracking()
            .SingleAsync(i => i.Id == invoiceId, ct);
        originalInvoice.Status.Should().Be(InvoiceStatus.Cancelled);
        originalInvoice.CancellationInfo.Should().NotBeNull();
        originalInvoice.CancellationInfo!.CreditNoteId.Should().Be(creditNoteId);

        // Projection row updated.
        var pending = await db.PendingCreditNotes.AsNoTracking()
            .SingleAsync(r => r.CorrelationId == sagaCorrelationId, ct);
        pending.IssuedCreditNoteId.Should().Be(creditNoteId);

        // Both Avro events fired on the same outbox topic, keyed by buyer.
        _fixture.OutboxSubstitute.Received(1).AddOutboxMessage(
            "invoicing.invoices",
            buyerId.ToString(),
            Arg.Is<global::Invoicing.CreditNotes.CreditNoteIssuedEvent>(e =>
                e.CreditNoteId == creditNoteId
                && e.OriginalInvoiceId == invoiceId
                && e.BuyerId == buyerId
                && e.CorrelationId == sagaCorrelationId));

        _fixture.OutboxSubstitute.Received(1).AddOutboxMessage(
            "invoicing.invoices",
            buyerId.ToString(),
            Arg.Is<global::Invoicing.Invoices.InvoiceCancelledEvent>(e =>
                e.InvoiceId == invoiceId
                && e.CreditNoteId == creditNoteId
                && e.BuyerId == buyerId
                && e.CorrelationId == sagaCorrelationId));
    }

    [Fact]
    public async Task Example_3_3_OriginalAlreadyCancelled_ReturnsResultFail_NoNewCreditNote()
    {
        var ct = TestContext.Current.CancellationToken;
        var orderId = Guid.CreateVersion7();
        var paymentId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();
        var firstCnCorrelationId = Guid.CreateVersion7();
        var secondCnCorrelationId = Guid.CreateVersion7();
        const decimal Total = 99.00m;
        const string Currency = "EUR";

        // Issue the invoice and then cancel it via the first credit note.
        var invoiceId = await IssueInvoiceAsync(
            Guid.CreateVersion7(), orderId, paymentId, buyerId, Total, Currency, ct);

        await SeedConvergedPendingCreditNoteAsync(
            firstCnCorrelationId, orderId, paymentId, buyerId, Total, Currency, ct);

        await using (var firstScope = _fixture.CreateScope())
        {
            var handler = firstScope.ServiceProvider
                .GetRequiredService<ICommandHandler<IssueCreditNoteCommand, Guid>>();
            var first = await handler.HandleAsync(
                new IssueCreditNoteCommand { CorrelationId = firstCnCorrelationId }, ct);
            first.IsSuccess.Should().BeTrue("first credit note succeeds and cancels the invoice");
        }

        // Seed a second pending_credit_notes row pointing to the SAME invoice (now Cancelled).
        await SeedConvergedPendingCreditNoteAsync(
            secondCnCorrelationId, orderId, paymentId, buyerId, Total, Currency, ct);

        _fixture.ResetOutboxSubstitute();

        await using var scope = _fixture.CreateScope();
        var handler2 = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<IssueCreditNoteCommand, Guid>>();
        var second = await handler2.HandleAsync(
            new IssueCreditNoteCommand { CorrelationId = secondCnCorrelationId }, ct);

        using var _ = new AssertionScope();
        second.IsFailed.Should().BeTrue("I-CN-1 — credit note against a cancelled invoice is rejected");
        second.Errors.Should().Contain(e => e.Message.Contains("cancelled", StringComparison.OrdinalIgnoreCase));

        await using var assertScope = _fixture.CreateScope();
        var db = assertScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        var cnCount = await db.CreditNotes.AsNoTracking()
            .Where(cn => cn.OriginalInvoiceId == invoiceId)
            .CountAsync(ct);
        cnCount.Should().Be(1, "no second credit note created");

        // No new outbox events (only the first credit note's emissions, which were
        // recorded BEFORE we reset the substitute).
        _fixture.OutboxSubstitute.DidNotReceiveWithAnyArgs().AddOutboxMessage(default!, default, default!);
    }

    private async Task<Guid> IssueInvoiceAsync(
        Guid correlationId,
        Guid orderId,
        Guid paymentId,
        Guid buyerId,
        decimal total,
        string currency,
        CancellationToken ct)
    {
        await SeedConvergedPendingInvoiceAsync(
            correlationId, orderId, paymentId, buyerId, total, currency, ct);

        await using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<IssueInvoiceCommand, Guid>>();
        var result = await handler.HandleAsync(
            new IssueInvoiceCommand { CorrelationId = correlationId }, ct);
        result.IsSuccess.Should().BeTrue("invoice seed must succeed for the credit-note tests to be meaningful");
        return result.Value;
    }

    private async Task SeedConvergedPendingInvoiceAsync(
        Guid correlationId,
        Guid orderId,
        Guid paymentId,
        Guid buyerId,
        decimal totalAmount,
        string currency,
        CancellationToken ct)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IInvoicingDbContext>();

        var orderPayload = JsonSerializer.Serialize(new
        {
            OrderId = orderId,
            CorrelationId = correlationId,
            BuyerId = buyerId,
            ConfirmedAtUtc = IntegrationTestFixture.FixedFakeNow.UtcDateTime,
            Items = new[]
            {
                new
                {
                    ProductId = Guid.CreateVersion7(),
                    Sku = "SKU-WIDGET-1",
                    Name = "Test Widget",
                    Quantity = 1,
                    UnitPriceAmount = totalAmount,
                    LineTotalAmount = totalAmount,
                },
            },
            TotalAmount = (decimal?)totalAmount,
            Currency = (string?)currency,
            BillingAddress = new
            {
                Street1 = "Main Street 1",
                Street2 = (string?)null,
                City = "Prague",
                State = (string?)null,
                PostalCode = "11000",
                CountryCode = "CZ",
            },
        });

        var paymentPayload = JsonSerializer.Serialize(new
        {
            CorrelationId = correlationId,
            UserId = buyerId,
            PaymentTransactionId = paymentId,
            AuthorizationId = "auth-test",
            Amount = totalAmount,
            Currency = currency,
            CapturedAtUtc = IntegrationTestFixture.FixedFakeNow.UtcDateTime,
        });

        db.PendingInvoices.Add(new PendingInvoice
        {
            CorrelationId = correlationId,
            OrderId = orderId,
            PaymentId = paymentId,
            BuyerId = buyerId,
            OrderPayload = orderPayload,
            PaymentPayload = paymentPayload,
            FirstSeenAtUtc = IntegrationTestFixture.FixedFakeNow,
            CompletedAtUtc = IntegrationTestFixture.FixedFakeNow,
            IssuedInvoiceId = null,
        });

        await db.SaveChangesAsync(ct);
    }

    private async Task SeedConvergedPendingCreditNoteAsync(
        Guid correlationId,
        Guid orderId,
        Guid paymentId,
        Guid buyerId,
        decimal refundedAmount,
        string currency,
        CancellationToken ct)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IInvoicingDbContext>();

        var orderPayload = JsonSerializer.Serialize(new
        {
            OrderId = orderId,
            CorrelationId = correlationId,
            BuyerId = buyerId,
            Reason = "BuyerCancelled",
            AtStatus = "Confirmed",
            CancelledAtUtc = IntegrationTestFixture.FixedFakeNow.UtcDateTime,
            Items = new[]
            {
                new
                {
                    ProductId = Guid.CreateVersion7(),
                    Sku = "SKU-WIDGET-1",
                    Name = "Test Widget",
                    Quantity = 1,
                    UnitPriceAmount = refundedAmount,
                    LineTotalAmount = refundedAmount,
                },
            },
            TotalAmount = (decimal?)refundedAmount,
            Currency = (string?)currency,
            BillingAddress = new
            {
                Street1 = "Main Street 1",
                Street2 = (string?)null,
                City = "Prague",
                State = (string?)null,
                PostalCode = "11000",
                CountryCode = "CZ",
            },
        });

        var paymentPayload = JsonSerializer.Serialize(new
        {
            CorrelationId = correlationId,
            UserId = buyerId,
            PaymentTransactionId = paymentId,
            RefundTransactionId = Guid.CreateVersion7(),
            RefundedAmount = refundedAmount,
            Currency = currency,
            RefundedAtUtc = IntegrationTestFixture.FixedFakeNow.UtcDateTime,
        });

        db.PendingCreditNotes.Add(new PendingCreditNote
        {
            CorrelationId = correlationId,
            OrderId = orderId,
            PaymentId = paymentId,
            BuyerId = buyerId,
            OrderPayload = orderPayload,
            PaymentPayload = paymentPayload,
            FirstSeenAtUtc = IntegrationTestFixture.FixedFakeNow,
            CompletedAtUtc = IntegrationTestFixture.FixedFakeNow,
            IssuedCreditNoteId = null,
        });

        await db.SaveChangesAsync(ct);
    }
}
