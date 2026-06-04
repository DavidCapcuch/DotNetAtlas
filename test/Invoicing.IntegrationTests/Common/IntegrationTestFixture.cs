using System.Text.Json;
using FastEndpoints.Testing;
using FluentResults;
using Invoicing.Application.Blobs;
using Invoicing.Application.Common.Data;
using Invoicing.Application.CreditNotes.IssueCreditNote;
using Invoicing.Application.CreditNotes.Projections;
using Invoicing.Application.Invoices.IssueInvoice;
using Invoicing.Application.Invoices.Projections;
using Invoicing.Application.Pdf;
using Invoicing.Domain.Common.ValueObjects;
using Invoicing.Infrastructure.Messaging.Kafka.Notifications;
using Invoicing.Infrastructure.Persistence.Database;
using KafkaFlow;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Platform.CQRS;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.Test.Framework;
using Platform.Test.Framework.Database;
using Respawn;
using Serilog;
using Serilog.Sinks.XUnit.Injectable;
using Serilog.Sinks.XUnit.Injectable.Abstract;
using Serilog.Sinks.XUnit.Injectable.Extensions;

namespace Invoicing.IntegrationTests.Common;

internal sealed class IntegrationTestCollection : TestCollection<IntegrationTestFixture>;

/// <summary>
/// FastEndpoints <see cref="AppFixture{TEntryPoint}"/> for the Invoicing host. Boots the real
/// <c>Invoicing.Api</c> composition root inside <c>UseEnvironment("Testing")</c>, swaps in test
/// doubles for the external adapters (<see cref="IPdfGenerator"/>,
/// <see cref="IBlobStore"/>, <see cref="ITransactionalOutbox{TContext}"/>), and points
/// <see cref="ConnectionStringsOptions.Invoicing"/> at a throwaway Postgres container whose
/// schema is materialised by the same idempotent V*.sql scripts Flyway runs in compose
/// (#269). Tests resolve handlers from <see cref="Services"/> and invoke them directly;
/// Kafka consumers are skipped (Program.cs guards <c>CreateKafkaBus().StartAsync()</c> with
/// <c>!app.Environment.IsTesting()</c>) and the <see cref="NotificationDeliveryStatusChangedEventKafkaHandler"/>
/// is invoked synchronously via <c>TestKafkaMessageContext</c> instead of through a real broker.
/// </summary>
[DisableWafCache]
public class IntegrationTestFixture : AppFixture<Program>
{
    /// <summary>
    /// Fixed "now" used by the projection-payload JSON the seed helpers persist (the
    /// shape mirrors what the producer-side handlers write — <c>ConfirmedAtUtc</c>,
    /// <c>CapturedAtUtc</c>, etc.). This is a date constant, NOT an injected clock: per
    /// ADR-0015 line 104 the Generic Host registers <see cref="TimeProvider.System"/>;
    /// tests construct <see cref="FakeTimeProvider"/> locally where determinism matters.
    /// </summary>
    public static readonly DateTimeOffset FixedFakeNow =
        new(2026, 04, 24, 09, 00, 00, TimeSpan.Zero);

    private readonly PostgreSqlTestContainer _dbContainer = new(
        databaseName: "Invoicing",
        sqlScriptsMigrationsPath: SolutionPaths.SqlScriptMigrationsDirectoryFor("services/Invoicing/Invoicing.Infrastructure"),
        new RespawnerOptions
        {
            SchemasToInclude = [InvoicingDbContext.DefaultSchemaName]
        });

    /// <summary>The shared NSubstitute outbox stub. Tests assert on its received calls.</summary>
    public ITransactionalOutbox<IInvoicingDbContext> OutboxSubstitute { get; } =
        Substitute.For<ITransactionalOutbox<IInvoicingDbContext>>();

    /// <summary>The shared NSubstitute PDF generator stub. Returns deterministic dummy bytes / hash.</summary>
    public IPdfGenerator PdfGeneratorSubstitute { get; } = BuildPdfGeneratorStub();

    /// <summary>The shared NSubstitute blob store stub. Returns a deterministic <see cref="PdfBlobRef"/>.</summary>
    public IBlobStore BlobStoreSubstitute { get; } = BuildBlobStoreStub();

    /// <summary>Connection string for tests that bypass the DbContext (e.g. raw SQL pre-staging).</summary>
    public string ConnectionString => _dbContainer.ConnectionString;

    protected override async ValueTask PreSetupAsync()
    {
        // Start sequentially: concurrent Docker.DotNet InspectContainerAsync calls over the
        // Windows named pipe interleave on the shared ChunkedReadStream and intermittently
        // raise "Invalid chunk header encountered".
        await _dbContainer.StartAsync();
    }

    protected override IHost ConfigureAppHost(IHostBuilder a)
    {
        a.ConfigureWebHost(webBuilder =>
        {
            webBuilder
                .UseSetting("ConnectionStrings:Invoicing", _dbContainer.ConnectionString)
                // Pin the buyer-portal base URL so M7's ViewInvoiceUrl assertion
                // (`https://invoicing.test/invoices/{invoiceId}`) is independent of the
                // production appsettings value (`invoicing.example.com`).
                .UseSetting("BuyerPortal:BaseUrl", "https://invoicing.test");
        });

        return base.ConfigureAppHost(a);
    }

    protected override void ConfigureApp(IWebHostBuilder a)
    {
        a
            .UseEnvironment("Testing")
            .ConfigureServices((context, services) =>
            {
                var injectableTestOutputSink = new InjectableTestOutputSink();
                services.AddSingleton<IInjectableTestOutputSink>(injectableTestOutputSink);
                services.AddSerilog((_, loggerConfiguration) =>
                {
                    loggerConfiguration
                        .MinimumLevel.Debug()
                        .ReadFrom.Configuration(context.Configuration)
                        .WriteTo.InjectableTestOutput(injectableTestOutputSink)
                        .Enrich.FromLogContext();
                }, true, true);
            })
            .ConfigureTestServices(services =>
            {
                // Replace the real Azurite-backed adapter with the NSubstitute stub. The
                // real adapter is exercised by the M3 integration tests in AzuriteFixture.
                services.RemoveAll<IBlobStore>();
                services.AddSingleton(BlobStoreSubstitute);

                // Replace the real QuestPdf-backed adapter with the NSubstitute stub. The
                // real adapter is exercised by the M4 QuestPdfInvoiceGeneratorTests.
                services.RemoveAll<IPdfGenerator>();
                services.AddSingleton(PdfGeneratorSubstitute);

                // Replace the platform Scoped TransactionalOutbox with our NSubstitute stub
                // so M7 tests can assert which Avro events the handlers enqueue without
                // standing up a Schema Registry container. Singleton lifetime matches the
                // pre-AppFixture wiring; KafkaFlow's typed handlers resolve it from the
                // request scope, which honours the singleton descriptor.
                services.RemoveAll<ITransactionalOutbox<IInvoicingDbContext>>();
                services.AddSingleton(OutboxSubstitute);
            });
    }

    /// <summary>Creates a per-test DI scope; caller disposes (supports <c>await using</c>).</summary>
    public AsyncServiceScope CreateScope() => Services.CreateAsyncScope();

    /// <summary>Resets the NSubstitute call recorder between tests.</summary>
    public void ResetOutboxSubstitute() => OutboxSubstitute.ClearReceivedCalls();

    /// <summary>Wipes every user table in the Invoicing schema between tests.</summary>
    public Task ResetFixtureStateAsync() => _dbContainer.CleanDataAsync();

    /// <summary>
    /// Seeds a fully-issued invoice by seeding a <see cref="PendingInvoice"/> projection row
    /// and running the real <c>IssueInvoiceCommandHandler</c>.
    /// Returns <c>(invoiceId, buyerId)</c> suitable for handler-under-test scenarios.
    /// </summary>
    /// <param name="clock">
    /// Clock the seed metadata is stamped with — pass <see cref="TimeProvider.System"/>
    /// when determinism doesn't matter, or a local <c>FakeTimeProvider</c> when the test
    /// pins time. The Generic Host registers <see cref="TimeProvider.System"/>, so the
    /// underlying <c>IssueInvoiceCommandHandler</c> always sees wall-clock; this parameter
    /// only affects the projection row's <c>FirstSeenAtUtc</c> / <c>CompletedAtUtc</c>.
    /// </param>
    public async Task<(Guid InvoiceId, Guid BuyerId)> SeedIssuedInvoiceAsync(TimeProvider clock, CancellationToken ct)
    {
        var orderId = Guid.CreateVersion7();
        var paymentId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();
        const decimal totalAmount = 100.00m;
        const string currency = "EUR";

        await SeedConvergedPendingInvoiceAsync(clock, orderId, paymentId, buyerId, totalAmount, currency, ct);

        await using var scope = Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<IssueInvoiceCommand, Guid>>();

        var result = await handler.HandleAsync(new IssueInvoiceCommand { OrderId = orderId }, ct);

        if (result.IsFailed)
        {
            throw new InvalidOperationException(
                $"SeedIssuedInvoiceAsync: IssueInvoiceCommandHandler failed — {string.Join("; ", result.Errors.Select(e => e.Message))}");
        }

        return (result.Value, buyerId);
    }

    /// <summary>
    /// Seeds a fully-delivered invoice. Issues first via <see cref="SeedIssuedInvoiceAsync"/>,
    /// then simulates the Notifications ack by invoking
    /// <see cref="NotificationDeliveryStatusChangedEventKafkaHandler"/> directly against the real
    /// Postgres container, correlating on the invoice's minted <c>delivery_notification_id</c>
    /// (ADR-0031). Resets the outbox call-recorder before returning so test assertions start from a
    /// clean baseline.
    /// </summary>
    public async Task<(Guid InvoiceId, Guid BuyerId)> SeedDeliveredInvoiceAsync(TimeProvider clock, CancellationToken ct)
    {
        var (invoiceId, buyerId) = await SeedIssuedInvoiceAsync(clock, ct);
        var notificationId = await GetDeliveryNotificationIdAsync(invoiceId, ct);

        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();

        // Wire the outbox stub's Database so EnsureTransactionAsync can open a real transaction.
        OutboxSubstitute.Database.Returns(dbContext.Database);

        var handler = scope.ServiceProvider.GetRequiredService<NotificationDeliveryStatusChangedEventKafkaHandler>();

        var ctx = Substitute.For<IMessageContext>();
        var consumerCtx = Substitute.For<IConsumerContext>();
        consumerCtx.WorkerStopped.Returns(ct);
        ctx.ConsumerContext.Returns(consumerCtx);

        await handler.Handle(ctx, new Notifications.NotificationDeliveryStatusChangedEvent
        {
            NotificationId = notificationId,
            RecipientUserId = buyerId,
            TemplateKey = "invoicing.invoice-delivered",
            Channel = "Email",
            Status = Notifications.NotificationDeliveryStatus.Dispatched,
            OccurredOnUtc = DateTime.UtcNow,
        });

        // Reset recorder so the test under construction starts with a clean call history.
        ResetOutboxSubstitute();

        return (invoiceId, buyerId);
    }

    /// <summary>Reads the NotificationId minted on the invoice when it was issued (ADR-0031).</summary>
    public async Task<Guid> GetDeliveryNotificationIdAsync(Guid invoiceId, CancellationToken ct)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IInvoicingDbContext>();
        var invoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId, ct);
        return invoice.DeliveryNotificationId
            ?? throw new InvalidOperationException($"Issued invoice {invoiceId} has no DeliveryNotificationId.");
    }

    /// <summary>
    /// Seeds a fully-issued credit note by first issuing an invoice, then seeding a
    /// <see cref="PendingCreditNote"/> against the same saga correlation, then running
    /// the real <c>IssueCreditNoteCommandHandler</c>. Returns <c>(CreditNoteId, BuyerId)</c>.
    /// Resets the outbox call recorder so the caller starts with a clean baseline.
    /// </summary>
    public async Task<(Guid CreditNoteId, Guid BuyerId)> SeedIssuedCreditNoteAsync(TimeProvider clock, CancellationToken ct)
    {
        var orderId = Guid.CreateVersion7();
        var paymentId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();
        const decimal totalAmount = 100.00m;
        const string currency = "EUR";

        // M7 invoice — prerequisite for the credit-note flow. Each step gets its own DI
        // scope (matching SeedDeliveredInvoiceAsync's pattern) so the invoice handler's
        // SaveChanges-time domain-event dispatcher completes before the credit-note scope
        // opens its own DbContext / outbox publisher chain.
        await SeedConvergedPendingInvoiceAsync(clock, orderId, paymentId, buyerId, totalAmount, currency, ct);
        await IssueInvoiceForCreditNoteSeedAsync(orderId, ct);

        await SeedConvergedPendingCreditNoteAsync(clock, orderId, paymentId, buyerId, totalAmount, currency, ct);
        var creditNoteId = await IssueCreditNoteForSeedAsync(orderId, ct);

        ResetOutboxSubstitute();
        return (creditNoteId, buyerId);
    }

    private async Task IssueInvoiceForCreditNoteSeedAsync(Guid orderId, CancellationToken ct)
    {
        await using var invoiceScope = Services.CreateAsyncScope();
        var invoiceHandler = invoiceScope.ServiceProvider
            .GetRequiredService<ICommandHandler<IssueInvoiceCommand, Guid>>();
        var invoiceResult = await invoiceHandler.HandleAsync(
            new IssueInvoiceCommand { OrderId = orderId }, ct);
        if (invoiceResult.IsFailed)
        {
            throw new InvalidOperationException(
                $"SeedIssuedCreditNoteAsync (invoice step) failed — {string.Join("; ", invoiceResult.Errors.Select(e => e.Message))}");
        }
    }

    private async Task<Guid> IssueCreditNoteForSeedAsync(Guid orderId, CancellationToken ct)
    {
        await using var creditScope = Services.CreateAsyncScope();
        var creditHandler = creditScope.ServiceProvider
            .GetRequiredService<ICommandHandler<IssueCreditNoteCommand, Guid>>();
        var creditResult = await creditHandler.HandleAsync(
            new IssueCreditNoteCommand { OrderId = orderId }, ct);
        if (creditResult.IsFailed)
        {
            throw new InvalidOperationException(
                $"SeedIssuedCreditNoteAsync (credit-note step) failed — {string.Join("; ", creditResult.Errors.Select(e => e.Message))}");
        }

        return creditResult.Value;
    }

    /// <summary>
    /// Seeds a <see cref="PendingCreditNote"/> projection row representing a converged
    /// (OrderCancelled + PaymentRefunded) state — the precondition for running
    /// <c>IssueCreditNoteCommandHandler</c>.
    /// </summary>
    private async Task SeedConvergedPendingCreditNoteAsync(
        TimeProvider clock,
        Guid orderId,
        Guid paymentId,
        Guid buyerId,
        decimal refundedAmount,
        string currency,
        CancellationToken ct)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IInvoicingDbContext>();
        var stampUtc = clock.GetUtcNow();

        var orderPayload = JsonSerializer.Serialize(new
        {
            OrderId = orderId,
            BuyerId = buyerId,
            Reason = "BuyerCancelled",
            AtStatus = "Confirmed",
            CancelledAtUtc = stampUtc.UtcDateTime,
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
            UserId = buyerId,
            PaymentTransactionId = paymentId,
            RefundTransactionId = Guid.CreateVersion7(),
            RefundedAmount = refundedAmount,
            Currency = currency,
            RefundedAtUtc = stampUtc.UtcDateTime,
        });

        db.PendingCreditNotes.Add(new PendingCreditNote
        {
            OrderId = orderId,
            PaymentId = paymentId,
            BuyerId = buyerId,
            OrderPayload = orderPayload,
            PaymentPayload = paymentPayload,
            FirstSeenAtUtc = stampUtc,
            CompletedAtUtc = stampUtc,
            IssuedCreditNoteId = null,
        });

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Seeds a <see cref="PendingInvoice"/> projection row representing a converged
    /// (Order + Payment) state — the precondition for running <c>IssueInvoiceCommandHandler</c>.
    /// </summary>
    public async Task SeedConvergedPendingInvoiceAsync(
        TimeProvider clock,
        Guid orderId,
        Guid paymentId,
        Guid buyerId,
        decimal totalAmount,
        string currency,
        CancellationToken ct)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IInvoicingDbContext>();
        var stampUtc = clock.GetUtcNow();

        var orderPayload = JsonSerializer.Serialize(new
        {
            OrderId = orderId,
            BuyerId = buyerId,
            ConfirmedAtUtc = stampUtc.UtcDateTime,
            Items = new[]
            {
                new
                {
                    ProductId = Guid.CreateVersion7(),
                    Sku = "SKU-SEED-1",
                    Name = "Seed Product",
                    Quantity = 1,
                    UnitPriceAmount = totalAmount,
                    LineTotalAmount = totalAmount,
                },
            },
            TotalAmount = (decimal?)totalAmount,
            Currency = (string?)currency,
            BillingAddress = new
            {
                Street1 = "Seed Street 1",
                Street2 = (string?)null,
                City = "Prague",
                State = (string?)null,
                PostalCode = "11000",
                CountryCode = "CZ",
            },
        });

        var paymentPayload = JsonSerializer.Serialize(new
        {
            UserId = buyerId,
            PaymentTransactionId = paymentId,
            AuthorizationId = "auth-seed",
            Amount = totalAmount,
            Currency = currency,
            CapturedAtUtc = stampUtc.UtcDateTime,
        });

        db.PendingInvoices.Add(new PendingInvoice
        {
            OrderId = orderId,
            PaymentId = paymentId,
            BuyerId = buyerId,
            OrderPayload = orderPayload,
            PaymentPayload = paymentPayload,
            FirstSeenAtUtc = stampUtc,
            CompletedAtUtc = stampUtc,
            IssuedInvoiceId = null,
        });

        await db.SaveChangesAsync(ct);
    }

    protected override async ValueTask TearDownAsync()
    {
        await _dbContainer.DisposeAsync();
    }

    private static IPdfGenerator BuildPdfGeneratorStub()
    {
        const string DummyHash = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
        var dummyBytes = "%PDF-test\n"u8.ToArray();
        var result = new PdfGenerationResult(dummyBytes, DummyHash, dummyBytes.LongLength, "application/pdf");

        var stub = Substitute.For<IPdfGenerator>();
        stub.GenerateInvoiceAsync(Arg.Any<Invoicing.Domain.Invoices.Invoice>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(result));
        stub.GenerateCreditNoteAsync(Arg.Any<Invoicing.Domain.CreditNotes.CreditNote>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(result));
        return stub;
    }

    private static IBlobStore BuildBlobStoreStub()
    {
        const string DummyHash = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
        var stub = Substitute.For<IBlobStore>();
        stub.UploadAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var blobName = call.ArgAt<string>(1);
                var size = call.ArgAt<ReadOnlyMemory<byte>>(2).Length;
                var refResult = PdfBlobRef.Create(blobName, DummyHash, Math.Max(1, size));
                if (refResult.IsFailed)
                {
                    throw new InvalidOperationException(
                        "PdfBlobRef.Create stub failed: " + string.Join("; ", refResult.Errors.Select(e => e.Message)));
                }

                return refResult.Value;
            });

        // Read-side query handlers (GetInvoiceById, GetInvoiceByOrderId, GetInvoicesByBuyer,
        // GetCreditNoteById) mint a per-request SAS URL on every fetch. The returned URI
        // echoes the container + blob name so tests can assert the mapper used the right
        // PdfBlobRef.BlobName without a real Azurite round-trip.
        stub.GetSasUrlAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var container = call.ArgAt<string>(0);
                var blobName = call.ArgAt<string>(1);
                return new Uri($"https://test-blob.local/{container}/{blobName}?sig=test");
            });
        return stub;
    }
}
