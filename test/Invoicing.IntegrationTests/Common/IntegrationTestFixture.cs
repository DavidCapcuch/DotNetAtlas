using EntityFramework.Exceptions.PostgreSQL;
using FluentResults;
using Invoicing.Application.Blobs;
using Invoicing.Application.Common;
using Invoicing.Application.Common.Data;
using Invoicing.Application.Common.Numbering;
using Invoicing.Application.Pdf;
using Invoicing.Domain.Common.ValueObjects;
using Invoicing.Infrastructure.Persistence.Database;
using Invoicing.Infrastructure.Persistence.Database.Interceptors;
using Invoicing.Infrastructure.Persistence.Numbering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
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
                ["InvoicingTopics:DltTopicSuffix"] = ".Invoicing.DLT",
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
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var blobName = call.ArgAt<string>(1);
                var size = call.ArgAt<ReadOnlyMemory<byte>>(2).Length;
                var blobUri = new Uri($"https://test.blob.local/invoices-test/{blobName}?sv=stub-sas");
                var refResult = PdfBlobRef.Create(blobUri, DummyHash, Math.Max(1, size));
                if (refResult.IsFailed)
                {
                    throw new InvalidOperationException(
                        "PdfBlobRef.Create stub failed: " + string.Join("; ", refResult.Errors.Select(e => e.Message)));
                }

                return refResult.Value;
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
