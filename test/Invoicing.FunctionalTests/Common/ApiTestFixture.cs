using FastEndpoints.Testing;
using Invoicing.Application.Blobs;
using Invoicing.Domain.Common.ValueObjects;
using Invoicing.FunctionalTests.Common.TestClientInfrastructure;
using Invoicing.Infrastructure.Persistence.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using OpenTelemetry;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.Test.Framework;
using Platform.Test.Framework.Auth;
using Platform.Test.Framework.Database;
using Platform.Test.Framework.Kafka;
using Platform.Test.Framework.Redis;
using Respawn;
using Serilog;
using Serilog.Sinks.XUnit.Injectable;
using Serilog.Sinks.XUnit.Injectable.Abstract;
using Serilog.Sinks.XUnit.Injectable.Extensions;

namespace Invoicing.FunctionalTests.Common;

internal sealed class FunctionalTestCollection : TestCollection<ApiTestFixture>;

/// <summary>
/// FastEndpoints <see cref="AppFixture{TEntryPoint}"/> for the Invoicing API. Spins up
/// Postgres + Redis Testcontainers. Schema comes from the same idempotent V*.sql scripts
/// Flyway runs in compose (#269) — Integration and Functional fixtures share one source
/// of truth instead of diverging on EnsureCreatedAsync vs MigrateAsync.
/// Forces <c>ASPNETCORE_ENVIRONMENT=Testing</c> so the host skips the Kafka enrichment
/// consumers, replaces <see cref="IBlobStore"/> with an NSubstitute fake (M3's Azurite
/// roundtrip is exercised by the integration suite), and replaces the schema-registry-backed
/// <see cref="IOutboxWriter"/> with <see cref="FakeOutboxWriter"/> so seeded
/// <c>Invoice.Issue</c> domain events do not blow up reaching for a non-existent registry.
/// </summary>
[DisableWafCache]
public class ApiTestFixture : AppFixture<Program>
{
    private readonly PostgreSqlTestContainer _dbContainer = new(
        databaseName: "Invoicing",
        sqlScriptsMigrationsPath: SolutionPaths.SqlScriptMigrationsDirectoryFor("services/Invoicing/Invoicing.Infrastructure"),
        new RespawnerOptions
        {
            SchemasToInclude = [InvoicingDbContext.DefaultSchemaName]
        });

    private readonly RedisTestContainer _redisContainer = new();

    private readonly FakeTokenSigner _signer = new(audience: "invoicing-service-tests");

    public HttpClientRegistry<Program> HttpClientRegistry { get; private set; } = null!;

    public FakeTokenCreator TokenCreator { get; private set; } = null!;

    /// <summary>
    /// NSubstitute fake for <see cref="IBlobStore"/> — returns a deterministic SAS URL on
    /// <c>GetSasUrlAsync</c> so M8 query handlers can exercise the URL-minting code path
    /// without standing up Azurite. The real adapter is exercised by the M3 integration
    /// tests in <c>AzuriteFixture</c>.
    /// </summary>
    public IBlobStore BlobStoreSubstitute { get; } = BuildBlobStoreStub();

    protected override async ValueTask PreSetupAsync()
    {
        // Start sequentially: concurrent Docker.DotNet InspectContainerAsync calls over the
        // Windows named pipe interleave on the shared ChunkedReadStream and intermittently
        // raise "Invalid chunk header encountered".
        await _dbContainer.StartAsync();
        await _redisContainer.StartAsync();
    }

    protected override ValueTask SetupAsync()
    {
        TokenCreator = new FakeTokenCreator(_signer);
        HttpClientRegistry = new HttpClientRegistry<Program>(this, TokenCreator);
        return ValueTask.CompletedTask;
    }

    protected override IHost ConfigureAppHost(IHostBuilder a)
    {
        a.ConfigureWebHost(webBuilder =>
        {
            webBuilder
                .UseSetting("ConnectionStrings:Invoicing", _dbContainer.ConnectionString)
                .UseSetting("ConnectionStrings:Redis:Cache", _redisContainer.ConnectionString);
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
                // Replace the real Azurite-backed adapter with the NSubstitute fake so
                // GET endpoints can exercise SAS-URL minting without standing up
                // Azurite. The real roundtrip is M3's territory.
                services.RemoveAll<IBlobStore>();
                services.AddSingleton(BlobStoreSubstitute);

                // Replace the schema-registry-backed outbox writer with an in-memory
                // fake. The seed helper invokes Invoice.Issue which raises
                // InvoiceIssuedDomainEvent; the M7 outbox publisher domain-event
                // handler picks that up and would otherwise serialise the Avro event
                // against a non-existent registry. Asserting on Kafka messages is M7's
                // job; M8 only needs the HTTP surface to round-trip.
                services.RemoveAll<IOutboxWriter>();
                services.AddSingleton<IOutboxWriter, FakeOutboxWriter>();

                // Wire the JwtBearer scheme to trust _signer's RSA key — keeps
                // every TokenValidationParameters flag at its production default
                // of TRUE. See Platform.Test.Framework.Auth.JwtBearerTestExtensions.
                services.ConfigureJwtBearerForTests(_signer);
            });
    }

    public async Task ResetFixtureStateAsync()
    {
        using var _ = SuppressInstrumentationScope.Begin();

        // Respawn (inside PostgreSqlTestContainer) wipes every user table in the
        // invoicing schema in dependency order; Redis flushes its own keyspace.
        await Task.WhenAll(
            _dbContainer.CleanDataAsync(),
            _redisContainer.CleanDataAsync());
    }

    protected override async ValueTask TearDownAsync()
    {
        _signer.Dispose();
        await _dbContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }

    private static IBlobStore BuildBlobStoreStub()
    {
        const string DummyHash = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
        var stub = Substitute.For<IBlobStore>();

        stub.GetSasUrlAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var container = call.ArgAt<string>(0);
                var blobName = call.ArgAt<string>(1);
                return new Uri($"https://test.blob.local/{container}/{blobName}?sv=stub-fresh-sas");
            });

        // Upload + Download are not exercised by M8 endpoints, but seed helpers may
        // (defensively) end up here if a future test path expands; provide a sane stub.
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
                return PdfBlobRef.Create(blobName, DummyHash, Math.Max(1, size)).Value;
            });

        return stub;
    }
}
