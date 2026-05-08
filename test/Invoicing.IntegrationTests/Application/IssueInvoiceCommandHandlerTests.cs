using System.Text.Json;
using AwesomeAssertions;
using Invoicing.Application.Common.Data;
using Invoicing.Application.Invoices.IssueInvoice;
using Invoicing.Application.Invoices.Projections;
using Invoicing.Domain.Common.Errors;
using Invoicing.Domain.Invoices.ValueObjects;
using Invoicing.Infrastructure.Persistence.Database;
using Invoicing.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Platform.CQRS;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Exceptions;
using Xunit;

namespace Invoicing.IntegrationTests.Application;

/// <summary>
/// M7 integration tests for <c>IssueInvoiceCommandHandler</c> against a real Postgres
/// container. Exercises the example-mapping sessions:
/// <list type="bullet">
///   <item>1.1 — happy path (order + payment converged → invoice issued, allocator advances, outbox fires).</item>
///   <item>1.3 — idempotent re-issue (already-set <c>IssuedInvoiceId</c> → no-op).</item>
///   <item>1.4 — total mismatch (Order.Total ≠ Payment.Amount → <c>DataIntegrityException</c> bug-class).</item>
/// </list>
/// PDF generation + blob upload + Avro outbox serialisation are stubbed at the fixture
/// level (see <c>IntegrationTestFixture</c>); the assertions verify what the M7 handler
/// SHOULD have invoked rather than what the third-party adapters produce.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class IssueInvoiceCommandHandlerTests
{
    private readonly IntegrationTestFixture _fixture;

    public IssueInvoiceCommandHandlerTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.ResetOutboxSubstitute();
    }

    [Fact]
    public async Task Example_1_1_HappyPath_IssuesInvoice_AdvancesAllocator_AndEnqueuesOutbox()
    {
        var ct = TestContext.Current.CancellationToken;
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var paymentId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();

        await SeedConvergedPendingInvoiceAsync(
            correlationId, orderId, paymentId, buyerId, totalAmount: 152.00m, currency: "EUR", ct);

        await using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<IssueInvoiceCommand, Guid>>();

        var result = await handler.HandleAsync(
            new IssueInvoiceCommand { CorrelationId = correlationId },
            ct);

        using var _ = new AssertionScope();
        result.IsSuccess.Should().BeTrue();
        var invoiceId = result.Value;

        // Invoice aggregate persisted in Issued status with the allocated number.
        await using var assertScope = _fixture.CreateScope();
        var db = assertScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        var invoice = await db.Invoices
            .Include(i => i.Lines)
            .Include(i => i.VatLines)
            .AsNoTracking()
            .SingleAsync(i => i.Id == invoiceId, ct);

        invoice.Status.Should().Be(InvoiceStatus.Issued);
        invoice.InvoiceNumber.Should().NotBeNull();
        invoice.InvoiceNumber!.Value.Should().MatchRegex(@"^INV-2026-\d{6}$");
        invoice.OrderId.Should().Be(orderId);
        invoice.PaymentId.Should().Be(paymentId);
        invoice.BuyerId.Should().Be(buyerId);
        invoice.CorrelationId.Should().Be(correlationId);
        invoice.Total.Amount.Should().Be(152.00m);
        invoice.Total.Currency.Name.Should().Be("EUR");
        invoice.Lines.Should().HaveCount(1);
        invoice.PdfBlobRef.Should().NotBeNull();
        invoice.PdfBlobRef!.BlobUri.AbsoluteUri.Should().StartWith("https://test.blob.local/invoices-test/");
        invoice.IssueDate.Should().Be(IntegrationTestFixture.FixedFakeNow);

        // Projection row updated with the issued invoice id.
        var pending = await db.PendingInvoices.AsNoTracking()
            .SingleAsync(r => r.CorrelationId == correlationId, ct);
        pending.IssuedInvoiceId.Should().Be(invoiceId);

        // Allocator advanced by exactly one for fiscal year 2026.
        var allocator = await db.InvoiceNumberAllocators.AsNoTracking()
            .SingleAsync(a => a.Year == 2026, ct);
        allocator.NextValue.Should().Be(invoice.InvoiceNumber.Sequence + 1);

        // Outbox substitute received exactly one InvoiceIssuedEvent keyed by BuyerId.
        _fixture.OutboxSubstitute.Received(1).AddOutboxMessage(
            "invoicing.invoices",
            buyerId.ToString(),
            Arg.Is<global::Invoicing.Invoices.InvoiceIssuedEvent>(e =>
                e.InvoiceId == invoiceId
                && e.OrderId == orderId
                && e.PaymentId == paymentId
                && e.BuyerId == buyerId
                && e.CorrelationId == correlationId));

        // No InvoiceCancelledEvent — credit-note flow didn't run.
        _fixture.OutboxSubstitute.DidNotReceive().AddOutboxMessage(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<global::Invoicing.Invoices.InvoiceCancelledEvent>());
    }

    [Fact]
    public async Task Example_1_3_AlreadyIssued_ShortCircuits_NoDuplicateInvoice()
    {
        var ct = TestContext.Current.CancellationToken;
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var paymentId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();

        await SeedConvergedPendingInvoiceAsync(
            correlationId, orderId, paymentId, buyerId, totalAmount: 99.00m, currency: "EUR", ct);

        await using (var firstScope = _fixture.CreateScope())
        {
            var handler = firstScope.ServiceProvider
                .GetRequiredService<ICommandHandler<IssueInvoiceCommand, Guid>>();
            var first = await handler.HandleAsync(
                new IssueInvoiceCommand { CorrelationId = correlationId }, ct);
            first.IsSuccess.Should().BeTrue();
        }

        // Snapshot the allocator value after the first issuance so we can assert no advance.
        long allocatorBeforeReplay;
        Guid invoiceIdBefore;
        await using (var preScope = _fixture.CreateScope())
        {
            var db = preScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            allocatorBeforeReplay = (await db.InvoiceNumberAllocators.AsNoTracking()
                .SingleAsync(a => a.Year == 2026, ct)).NextValue;
            invoiceIdBefore = (await db.PendingInvoices.AsNoTracking()
                .SingleAsync(r => r.CorrelationId == correlationId, ct)).IssuedInvoiceId!.Value;
        }

        _fixture.ResetOutboxSubstitute();

        // Second invocation must short-circuit on IssuedInvoiceId.
        await using (var replayScope = _fixture.CreateScope())
        {
            var handler = replayScope.ServiceProvider
                .GetRequiredService<ICommandHandler<IssueInvoiceCommand, Guid>>();
            var replay = await handler.HandleAsync(
                new IssueInvoiceCommand { CorrelationId = correlationId }, ct);

            using var _ = new AssertionScope();
            replay.IsSuccess.Should().BeTrue();
            replay.Value.Should().Be(invoiceIdBefore, "replay returns the existing invoice id");
        }

        await using (var assertScope = _fixture.CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var invoiceCount = await db.Invoices
                .AsNoTracking()
                .Where(i => i.CorrelationId == correlationId)
                .CountAsync(ct);
            invoiceCount.Should().Be(1, "no duplicate invoice on replay");

            var allocatorAfter = (await db.InvoiceNumberAllocators.AsNoTracking()
                .SingleAsync(a => a.Year == 2026, ct)).NextValue;
            allocatorAfter.Should().Be(allocatorBeforeReplay, "allocator never advances on replay");
        }

        _fixture.OutboxSubstitute.DidNotReceiveWithAnyArgs().AddOutboxMessage(default!, default, default!);
    }

    [Fact]
    public async Task JoinsAmbientTransaction_DoesNotNest_When_Outer_BeginTransaction_Already_Open()
    {
        // Locks down the M7 transaction-shape contract that the M6 consumers depend on:
        // when the inbox middleware has already opened a transaction, the M7 handler must
        // detect Database.CurrentTransaction != null and SKIP its own BeginTransactionAsync
        // (Npgsql doesn't support nested transactions; nesting would throw). The handler must
        // also leave the outer transaction open — the test commits explicitly so the
        // assertions can observe the persisted Invoice. A regression here would either throw
        // on nested-begin or commit prematurely (so a rollback by the outer caller would not
        // undo the issuance).
        var ct = TestContext.Current.CancellationToken;
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var paymentId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();

        await SeedConvergedPendingInvoiceAsync(
            correlationId, orderId, paymentId, buyerId, totalAmount: 99.00m, currency: "EUR", ct);

        Guid invoiceId;
        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var handler = scope.ServiceProvider
                .GetRequiredService<ICommandHandler<IssueInvoiceCommand, Guid>>();

            // Simulate the inbox middleware's enclosing transaction.
            await using var outer = await db.Database.BeginTransactionAsync(ct);

            var result = await handler.HandleAsync(
                new IssueInvoiceCommand { CorrelationId = correlationId }, ct);

            using (new AssertionScope())
            {
                result.IsSuccess.Should().BeTrue("the handler must succeed when joining an outer transaction");
                invoiceId = result.Value;

                // Outer transaction is still open — the handler did NOT commit it.
                db.Database.CurrentTransaction.Should().NotBeNull("the handler must not commit the outer transaction");
            }

            await outer.CommitAsync(ct);
        }

        // After the outer commit, the invoice is visible.
        await using (var assertScope = _fixture.CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
            var invoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId, ct);
            invoice.Status.Should().Be(InvoiceStatus.Issued);
        }

        _fixture.OutboxSubstitute.Received(1).AddOutboxMessage(
            "invoicing.invoices",
            buyerId.ToString(),
            Arg.Is<global::Invoicing.Invoices.InvoiceIssuedEvent>(e => e.InvoiceId == invoiceId));
    }

    [Fact]
    public async Task Example_1_4_TotalMismatch_ThrowsDataIntegrityException_NoInvoiceIssued()
    {
        var ct = TestContext.Current.CancellationToken;
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var paymentId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();

        // Seed the projection row with mismatched amounts: order = €152, payment = €150.
        await SeedConvergedPendingInvoiceAsync(
            correlationId, orderId, paymentId, buyerId,
            totalAmount: 152.00m, currency: "EUR", ct, paymentAmountOverride: 150.00m);

        await using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<IssueInvoiceCommand, Guid>>();

        var act = async () => await handler.HandleAsync(
            new IssueInvoiceCommand { CorrelationId = correlationId }, ct);

        using var _ = new AssertionScope();
        await act.Should().ThrowAsync<DataIntegrityException>()
            .Where(ex => ex.Message.Contains("152", StringComparison.Ordinal)
                || ex.Message.Contains("150", StringComparison.Ordinal));

        await using var assertScope = _fixture.CreateScope();
        var db = assertScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        var invoiceCount = await db.Invoices.AsNoTracking()
            .Where(i => i.CorrelationId == correlationId)
            .CountAsync(ct);
        invoiceCount.Should().Be(0, "mismatch must NOT issue an invoice");

        var pending = await db.PendingInvoices.AsNoTracking()
            .SingleAsync(r => r.CorrelationId == correlationId, ct);
        pending.IssuedInvoiceId.Should().BeNull("projection row stays unissued on mismatch");

        _fixture.OutboxSubstitute.DidNotReceiveWithAnyArgs().AddOutboxMessage(default!, default, default!);
    }

    private async Task SeedConvergedPendingInvoiceAsync(
        Guid correlationId,
        Guid orderId,
        Guid paymentId,
        Guid buyerId,
        decimal totalAmount,
        string currency,
        CancellationToken ct,
        decimal? paymentAmountOverride = null)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IInvoicingDbContext>();

        // Mirror the JSON shape that the M6 producer-side handlers persist.
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
            Amount = paymentAmountOverride ?? totalAmount,
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
}
