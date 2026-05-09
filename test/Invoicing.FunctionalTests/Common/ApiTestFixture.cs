using FastEndpoints.Testing;
using Invoicing.Application.Blobs;
using Invoicing.Domain.Common.ValueObjects;
using Invoicing.FunctionalTests.Common.TestClientInfrastructure;
using Invoicing.Infrastructure.Persistence.Database;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.Test.Framework.Kafka;
using Platform.Test.Framework.Redis;
using Serilog;
using Serilog.Sinks.XUnit.Injectable;
using Serilog.Sinks.XUnit.Injectable.Abstract;
using Serilog.Sinks.XUnit.Injectable.Extensions;
using Testcontainers.PostgreSql;

namespace Invoicing.FunctionalTests.Common;

[CollectionDefinition(nameof(FunctionalTestCollection))]
public sealed class FunctionalTestCollection : TestCollection<ApiTestFixture>;

/// <summary>
/// FastEndpoints <see cref="AppFixture{TEntryPoint}"/> for the Invoicing API. Spins up
/// Postgres + Redis Testcontainers, applies EF Core migrations programmatically (the
/// schema is committed under <c>Invoicing.Infrastructure/Persistence/Database/Migrations</c>),
/// forces <c>ASPNETCORE_ENVIRONMENT=Testing</c> so the host skips the Kafka enrichment
/// consumers, replaces <see cref="IBlobStore"/> with an NSubstitute fake (M3's Azurite
/// roundtrip is exercised by the integration suite), and replaces the schema-registry-backed
/// <see cref="IOutboxWriter"/> with <see cref="FakeOutboxWriter"/> so seeded
/// <c>Invoice.Issue</c> domain events do not blow up reaching for a non-existent registry.
/// </summary>
[DisableWafCache]
public class ApiTestFixture : AppFixture<Program>
{
    private readonly PostgreSqlContainer _pgContainer = new PostgreSqlBuilder("postgres:18.3")
        .WithDatabase("Invoicing")
        .WithUsername("postgres")
        .WithPassword("TestingPasswordThatShouldBeInVault123!")
        .WithCleanUp(true)
        .Build();

    private readonly RedisTestContainer _redisContainer = new();

    /// <summary>
    /// Pinned to 2026-04-23 10:00 UTC so issue-date / SAS-expiry assertions stay
    /// deterministic.
    /// </summary>
    public FakeTimeProvider FakeTime { get; } = new(
        new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero));

    public HttpClientRegistry<Program> HttpClientRegistry { get; private set; } = null!;

    /// <summary>
    /// NSubstitute fake for <see cref="IBlobStore"/> — returns a deterministic SAS URL on
    /// <c>GetSasUrlAsync</c> so M8 query handlers can exercise the URL-minting code path
    /// without standing up Azurite. The real adapter is exercised by the M3 integration
    /// tests in <c>AzuriteFixture</c>.
    /// </summary>
    public IBlobStore BlobStoreSubstitute { get; } = BuildBlobStoreStub();

    protected override async ValueTask PreSetupAsync()
    {
        // Start sequentially: concurrent Docker.DotNet InspectContainerAsync calls over
        // the Windows named pipe interleave on the shared ChunkedReadStream and
        // intermittently raise "Invalid chunk header encountered". Mirrors the Ordering
        // fixture's reasoning.
        await _pgContainer.StartAsync();
        await _redisContainer.StartAsync();
    }

    protected override async ValueTask SetupAsync()
    {
        HttpClientRegistry = new HttpClientRegistry<Program>(this);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        await db.Database.MigrateAsync();
    }

    protected override IHost ConfigureAppHost(IHostBuilder a)
    {
        a.ConfigureWebHost(webBuilder =>
        {
            webBuilder
                .UseSetting("ConnectionStrings:Invoicing", _pgContainer.GetConnectionString())
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
                // Pin time so SAS-expiry / IssueDate assertions are stable.
                services.AddSingleton<TimeProvider>(FakeTime);

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

                // Relax JWT validation for the test host. Configure
                // (IConfigureNamedOptions) runs before the framework's
                // JwtBearerPostConfigureOptions, so RequireHttpsMetadata=false is
                // observed before the HTTPS-authority check fires. PostConfigure would
                // run after the framework's post-configure had already thrown.
                services.Configure<JwtBearerOptions>(
                    JwtBearerDefaults.AuthenticationScheme,
                    options =>
                    {
                        options.RequireHttpsMetadata = false;
                        // Authority + MetadataAddress are non-nullable; clear them so
                        // the test host doesn't try to fetch the OIDC discovery doc
                        // from a non-existent Keycloak.
                        options.Authority = string.Empty;
                        options.MetadataAddress = string.Empty;
#pragma warning disable CA5404 // Test host only — never executed in deployed environments.
                        options.TokenValidationParameters.ValidateIssuer = false;
                        options.TokenValidationParameters.ValidateAudience = false;
                        options.TokenValidationParameters.ValidateLifetime = false;
#pragma warning restore CA5404
                        options.TokenValidationParameters.ValidateIssuerSigningKey = false;
                        options.TokenValidationParameters.RequireSignedTokens = false;
                        options.TokenValidationParameters.SignatureValidator = (token, _) =>
                            new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(token);
                    });
            });
    }

    public async Task ResetFixtureStateAsync()
    {
        // Wipe Redis so the idempotency cache from a prior test does not poison the
        // next one. Postgres state is wiped by truncating the invoicing schema's user
        // tables.
        await _redisContainer.CleanDataAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            $"""
             TRUNCATE TABLE
                 "{InvoicingDbContext.DefaultSchemaName}"."credit_note_lines",
                 "{InvoicingDbContext.DefaultSchemaName}"."credit_notes",
                 "{InvoicingDbContext.DefaultSchemaName}"."invoice_lines",
                 "{InvoicingDbContext.DefaultSchemaName}"."invoice_vat_lines",
                 "{InvoicingDbContext.DefaultSchemaName}"."invoices",
                 "{InvoicingDbContext.DefaultSchemaName}"."pending_invoices",
                 "{InvoicingDbContext.DefaultSchemaName}"."pending_credit_notes",
                 "{InvoicingDbContext.DefaultSchemaName}"."OutboxMessages",
                 "{InvoicingDbContext.DefaultSchemaName}"."InboxMessages"
             RESTART IDENTITY CASCADE;
             """);
    }

    protected override async ValueTask TearDownAsync()
    {
        await _pgContainer.DisposeAsync();
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
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var blobName = call.ArgAt<string>(1);
                var size = call.ArgAt<ReadOnlyMemory<byte>>(2).Length;
                var blobUri = new Uri($"https://test.blob.local/invoices/{blobName}?sv=stub-upload-sas");
                return PdfBlobRef.Create(blobUri, DummyHash, Math.Max(1, size)).Value;
            });

        return stub;
    }
}
