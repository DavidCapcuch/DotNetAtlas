using System.Text.Json;
using EntityFramework.Exceptions.PostgreSQL;
using FluentResults;
using Invoicing.Application.Blobs;
using Invoicing.Application.Common;
using Invoicing.Application.Common.Data;
using Invoicing.Application.Common.Numbering;
using Invoicing.Application.CreditNotes.IssueCreditNote;
using Invoicing.Application.CreditNotes.Projections;
using Invoicing.Application.Invoices.IssueInvoice;
using Invoicing.Application.Invoices.Projections;
using Invoicing.Application.Pdf;
using Invoicing.Domain.Common.ValueObjects;
using Invoicing.Infrastructure.Messaging.Kafka.Notifications;
using Invoicing.Infrastructure.Persistence.Database;
using Invoicing.Infrastructure.Persistence.Database.Interceptors;
using Invoicing.Infrastructure.Persistence.Numbering;
using KafkaFlow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Platform.CQRS;
using Platform.ReliableMessaging.Outbox.EFCore;
using Testcontainers.PostgreSql;

namespace Invoicing.IntegrationTests.Common;

/// <summary>
/// Spins a throwaway Postgres container per collection and wires the M5/M6/M7
/// persistence + application slices. M5 brought <see cref="InvoicingDbContext"/> +
/// the gap-free allocators; M6 added the projection tables + inbox; M7 adds the
/// <c>Invoices</c> + <c>CreditNotes</c> aggregate tables, the
/// <see cref="DispatchDomainEventsInterceptor"/>, the Application-layer command
/// handlers + outbox publishers, and a stubbed <see cref="ITransactionalOutbox{TContext}"/>
/// so M7 tests can assert the correct external Avro events were enqueued without
/// standing up a real Confluent Schema Registry.
/// </summary>
/// <remarks>
/// Schema is materialised via <see cref="DatabaseFacade.EnsureCreatedAsync"/>
/// rather than EF migrations — per CLAUDE.md the user generates production
/// migrations deterministically; tests derive the schema from the EF model
/// so the fixture stays self-contained. The blob store and PDF generator are
/// NSubstitute stubs at this level (lifecycle: singleton); the M3 integration
/// tests in <c>AzuriteFixture</c> exercise the real Azurite-backed adapter.
/// </remarks>
public sealed class IntegrationTestFixture : IAsyncLifetime
{
    /// <summary>Pinned to 2026-04-24 09:00 UTC so M7 assertions on issue dates / invoice-number year are deterministic.</summary>
    public static readonly DateTimeOffset FixedFakeNow =
        new(2026, 04, 24, 09, 00, 00, TimeSpan.Zero);

    private readonly PostgreSqlContainer _pgContainer = new PostgreSqlBuilder("postgres:18.3")
        .WithDatabase("Invoicing")
        .WithUsername("postgres")
        .WithPassword("TestingPasswordThatShouldBeInVault123!")
        .WithCleanUp(true)
        .Build();

    private ServiceProvider _rootServices = null!;

    /// <summary>Test-controlled clock pinned to <see cref="FixedFakeNow"/>; resolvable as <see cref="TimeProvider"/>.</summary>
    public FakeTimeProvider FakeTime { get; } = new(FixedFakeNow);

    /// <summary>The shared NSubstitute outbox stub. Tests assert on its received calls.</summary>
    public ITransactionalOutbox<IInvoicingDbContext> OutboxSubstitute { get; } =
        Substitute.For<ITransactionalOutbox<IInvoicingDbContext>>();

    /// <summary>The shared NSubstitute PDF generator stub. Returns deterministic dummy bytes / hash.</summary>
    public IPdfGenerator PdfGeneratorSubstitute { get; } = BuildPdfGeneratorStub();

    /// <summary>The shared NSubstitute blob store stub. Returns a deterministic <see cref="PdfBlobRef"/>.</summary>
    public IBlobStore BlobStoreSubstitute { get; } = BuildBlobStoreStub();

    public async ValueTask InitializeAsync()
    {
        await _pgContainer.StartAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();

        services.AddLogging(b => b.AddDebug().SetMinimumLevel(LogLevel.Warning));

        // Minimal in-memory configuration so InvoicingTopicsOptions + BlobStorageOptions bind.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InvoicingTopics:Invoices"] = "invoicing.invoices",
                ["InvoicingTopics:OrderingOrders"] = "ordering.orders",
                ["InvoicingTopics:PaymentsTransactions"] = "payments.transactions",
                ["InvoicingTopics:NotificationsEmailCommands"] = "notifications.email-commands",
                ["InvoicingTopics:NotificationsEmailEvents"] = "notifications.email-events",
                ["InvoicingTopics:DltTopicSuffix"] = ".Invoicing.DLT",
                ["BuyerPortal:BaseUrl"] = "https://invoicing.test",
                ["BlobStorage:InvoicesContainerName"] = "invoices-test",
                ["BlobStorage:ConnectionString"] = "UseDevelopmentStorage=true",
            })
            .Build();
        services.AddSingleton<IConfiguration>(config);

        services.AddSingleton<TimeProvider>(FakeTime);

        services.AddScoped<DispatchDomainEventsInterceptor>();

        services.AddDbContext<InvoicingDbContext>((sp, options) => options
            .UseNpgsql(_pgContainer.GetConnectionString(), npg => npg
                .MigrationsHistoryTable("__EFMigrationsHistory", InvoicingDbContext.DefaultSchemaName))
            .UseSnakeCaseNamingConvention()
            .UseExceptionProcessor()
            .AddInterceptors(sp.GetRequiredService<DispatchDomainEventsInterceptor>()));

        services.AddScoped<IInvoicingDbContext>(sp => sp.GetRequiredService<InvoicingDbContext>());

        // Real allocators (require an enclosing transaction per ADR-0018; M7 handlers
        // ensure that contract).
        services.AddScoped<IInvoiceNumberAllocator, PostgresInvoiceNumberAllocator>();
        services.AddScoped<ICreditNoteNumberAllocator, PostgresCreditNoteNumberAllocator>();

        // Stubbed PDF generator + blob store: M3/M4 own the real adapters via the
        // AzuriteFixture / QuestPdf integration tests respectively. Here we only need
        // a deterministic in-memory result so the M7 handler can flow end-to-end.
        services.AddSingleton(PdfGeneratorSubstitute);
        services.AddSingleton(BlobStoreSubstitute);

        // Stubbed transactional outbox. The Application-layer outbox publisher
        // domain-event handlers will resolve this stub; tests assert on the stub's
        // received AddOutboxMessage calls. Skipping AddOutbox(...) avoids wiring a
        // schema-registry container in tests.
        services.AddSingleton(OutboxSubstitute);

        // BlobStorageOptions: tests bind from the in-memory config above. Production
        // wires the connection string from ConnectionStrings:AzureStorage in
        // InfrastructureDependencyInjection; tests don't need that.
        services.AddOptions<BlobStorageOptions>()
            .BindConfiguration(BlobStorageOptions.SectionName)
            .ValidateDataAnnotations();

        // Real Application-layer DI: validators, command handlers, domain-event
        // handlers + dispatcher, behavior chain (Tracing→Logging→Metrics→Validation).
        services.AddInvoicingApplication();

        // Kafka typed handlers — registered Scoped to match production KafkaFlow wiring.
        // Tests resolve these directly and invoke Handle(...) without a middleware stack.
        services.AddScoped<EmailNotificationSentEventKafkaHandler>();

        _rootServices = services.BuildServiceProvider();

        await using var setupScope = _rootServices.CreateAsyncScope();
        var dbContext = setupScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        await dbContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Creates a per-test DI scope; caller disposes (supports <c>await using</c>).</summary>
    public AsyncServiceScope CreateScope() => _rootServices.CreateAsyncScope();

    /// <summary>Connection string for tests that bypass the DbContext (e.g. raw SQL pre-staging).</summary>
    public string ConnectionString => _pgContainer.GetConnectionString();

    /// <summary>Resets the NSubstitute call recorder between tests.</summary>
    public void ResetOutboxSubstitute() => OutboxSubstitute.ClearReceivedCalls();

    /// <summary>
    /// Seeds a fully-issued invoice by seeding a <see cref="PendingInvoice"/> projection row
    /// and running the real <c>IssueInvoiceCommandHandler</c>.
    /// Returns <c>(invoiceId, buyerId)</c> suitable for handler-under-test scenarios.
    /// </summary>
    public async Task<(Guid InvoiceId, Guid BuyerId)> SeedIssuedInvoiceAsync(CancellationToken ct)
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var paymentId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();
        const decimal totalAmount = 100.00m;
        const string currency = "EUR";

        await SeedConvergedPendingInvoiceAsync(correlationId, orderId, paymentId, buyerId, totalAmount, currency, ct);

        await using var scope = _rootServices.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<IssueInvoiceCommand, Guid>>();

        var result = await handler.HandleAsync(new IssueInvoiceCommand { CorrelationId = correlationId }, ct);

        if (result.IsFailed)
        {
            throw new InvalidOperationException(
                $"SeedIssuedInvoiceAsync: IssueInvoiceCommandHandler failed — {string.Join("; ", result.Errors.Select(e => e.Message))}");
        }

        return (result.Value, buyerId);
    }

    /// <summary>
    /// Seeds a fully-delivered invoice. Issues first via <see cref="SeedIssuedInvoiceAsync"/>,
    /// then simulates the Notifications ack by invoking <see cref="EmailNotificationSentEventKafkaHandler"/>
    /// directly against the real Postgres container. Resets the outbox call-recorder
    /// before returning so test assertions start from a clean baseline.
    /// </summary>
    public async Task<(Guid InvoiceId, Guid BuyerId)> SeedDeliveredInvoiceAsync(CancellationToken ct)
    {
        var (invoiceId, buyerId) = await SeedIssuedInvoiceAsync(ct);

        await using var scope = _rootServices.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();

        // Wire the outbox stub's Database so EnsureTransactionAsync can open a real transaction.
        OutboxSubstitute.Database.Returns(dbContext.Database);

        var handler = scope.ServiceProvider.GetRequiredService<EmailNotificationSentEventKafkaHandler>();

        var ctx = Substitute.For<IMessageContext>();
        var consumerCtx = Substitute.For<IConsumerContext>();
        consumerCtx.WorkerStopped.Returns(ct);
        ctx.ConsumerContext.Returns(consumerCtx);

        await handler.Handle(ctx, new Notifications.Email.EmailNotificationSentEvent
        {
            UserId = buyerId,
            TemplateId = "invoicing.invoice-delivered",
            IdempotencyKey = $"invoice-delivered-{invoiceId}-1",
            SentAtUtc = DateTime.UtcNow,
            OccurredOnUtc = DateTime.UtcNow,
        });

        // Reset recorder so the test under construction starts with a clean call history.
        ResetOutboxSubstitute();

        return (invoiceId, buyerId);
    }

    /// <summary>
    /// Seeds a fully-issued credit note by first issuing an invoice, then seeding a
    /// <see cref="PendingCreditNote"/> against the same saga correlation, then running
    /// the real <c>IssueCreditNoteCommandHandler</c>. Returns <c>(CreditNoteId, BuyerId)</c>.
    /// Resets the outbox call recorder so the caller starts with a clean baseline.
    /// </summary>
    public async Task<(Guid CreditNoteId, Guid BuyerId)> SeedIssuedCreditNoteAsync(CancellationToken ct)
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var paymentId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();
        const decimal totalAmount = 100.00m;
        const string currency = "EUR";

        // M7 invoice — prerequisite for the credit-note flow. Each step gets its own DI
        // scope (matching SeedDeliveredInvoiceAsync's pattern) so the invoice handler's
        // SaveChanges-time domain-event dispatcher completes before the credit-note scope
        // opens its own DbContext / outbox publisher chain.
        await SeedConvergedPendingInvoiceAsync(correlationId, orderId, paymentId, buyerId, totalAmount, currency, ct);
        await IssueInvoiceForCreditNoteSeedAsync(correlationId, ct);

        await SeedConvergedPendingCreditNoteAsync(correlationId, orderId, paymentId, buyerId, totalAmount, currency, ct);
        var creditNoteId = await IssueCreditNoteForSeedAsync(correlationId, ct);

        ResetOutboxSubstitute();
        return (creditNoteId, buyerId);
    }

    private async Task IssueInvoiceForCreditNoteSeedAsync(Guid correlationId, CancellationToken ct)
    {
        await using var invoiceScope = _rootServices.CreateAsyncScope();
        var invoiceHandler = invoiceScope.ServiceProvider
            .GetRequiredService<ICommandHandler<IssueInvoiceCommand, Guid>>();
        var invoiceResult = await invoiceHandler.HandleAsync(
            new IssueInvoiceCommand { CorrelationId = correlationId }, ct);
        if (invoiceResult.IsFailed)
        {
            throw new InvalidOperationException(
                $"SeedIssuedCreditNoteAsync (invoice step) failed — {string.Join("; ", invoiceResult.Errors.Select(e => e.Message))}");
        }
    }

    private async Task<Guid> IssueCreditNoteForSeedAsync(Guid correlationId, CancellationToken ct)
    {
        await using var creditScope = _rootServices.CreateAsyncScope();
        var creditHandler = creditScope.ServiceProvider
            .GetRequiredService<ICommandHandler<IssueCreditNoteCommand, Guid>>();
        var creditResult = await creditHandler.HandleAsync(
            new IssueCreditNoteCommand { CorrelationId = correlationId }, ct);
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
        Guid correlationId,
        Guid orderId,
        Guid paymentId,
        Guid buyerId,
        decimal refundedAmount,
        string currency,
        CancellationToken ct)
    {
        await using var scope = _rootServices.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IInvoicingDbContext>();

        var orderPayload = JsonSerializer.Serialize(new
        {
            OrderId = orderId,
            CorrelationId = correlationId,
            BuyerId = buyerId,
            Reason = "BuyerCancelled",
            AtStatus = "Confirmed",
            CancelledAtUtc = FixedFakeNow.UtcDateTime,
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
            RefundedAtUtc = FixedFakeNow.UtcDateTime,
        });

        db.PendingCreditNotes.Add(new PendingCreditNote
        {
            CorrelationId = correlationId,
            OrderId = orderId,
            PaymentId = paymentId,
            BuyerId = buyerId,
            OrderPayload = orderPayload,
            PaymentPayload = paymentPayload,
            FirstSeenAtUtc = FixedFakeNow,
            CompletedAtUtc = FixedFakeNow,
            IssuedCreditNoteId = null,
        });

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Seeds a <see cref="PendingInvoice"/> projection row representing a converged
    /// (Order + Payment) state — the precondition for running <c>IssueInvoiceCommandHandler</c>.
    /// </summary>
    public async Task SeedConvergedPendingInvoiceAsync(
        Guid correlationId,
        Guid orderId,
        Guid paymentId,
        Guid buyerId,
        decimal totalAmount,
        string currency,
        CancellationToken ct)
    {
        await using var scope = _rootServices.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IInvoicingDbContext>();

        var orderPayload = JsonSerializer.Serialize(new
        {
            OrderId = orderId,
            CorrelationId = correlationId,
            BuyerId = buyerId,
            ConfirmedAtUtc = FixedFakeNow.UtcDateTime,
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
            CorrelationId = correlationId,
            UserId = buyerId,
            PaymentTransactionId = paymentId,
            AuthorizationId = "auth-seed",
            Amount = totalAmount,
            Currency = currency,
            CapturedAtUtc = FixedFakeNow.UtcDateTime,
        });

        db.PendingInvoices.Add(new PendingInvoice
        {
            CorrelationId = correlationId,
            OrderId = orderId,
            PaymentId = paymentId,
            BuyerId = buyerId,
            OrderPayload = orderPayload,
            PaymentPayload = paymentPayload,
            FirstSeenAtUtc = FixedFakeNow,
            CompletedAtUtc = FixedFakeNow,
            IssuedInvoiceId = null,
        });

        await db.SaveChangesAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_rootServices is not null)
        {
            await _rootServices.DisposeAsync();
        }

        await _pgContainer.DisposeAsync();
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

/// <summary>
/// xUnit v3 collection definition scoping <see cref="IntegrationTestFixture"/>
/// — one Postgres container shared across all integration tests in the
/// <c>Invoicing-Integration</c> collection, fresh per run.
/// </summary>
[CollectionDefinition(nameof(IntegrationTestCollection))]
public sealed class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>;
