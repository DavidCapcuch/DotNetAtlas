using FastEndpoints.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Payments.Application.Abstractions;
using Payments.Infrastructure.ExternalServices.PaymentGateway;
using Payments.Infrastructure.Persistence.Database;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.Test.Framework;
using Platform.Test.Framework.Database;
using Platform.Test.Framework.Kafka;
using Respawn;
using Serilog;
using Serilog.Sinks.XUnit.Injectable;
using Serilog.Sinks.XUnit.Injectable.Abstract;
using Serilog.Sinks.XUnit.Injectable.Extensions;

namespace Payments.IntegrationTests.Common;

internal sealed class IntegrationTestCollection : TestCollection<IntegrationTestFixture>;

/// <summary>
/// xUnit fixture booting the real <c>Payments.Api</c> host inside a
/// <see cref="AppFixture{TEntryPoint}"/> against a throwaway Postgres testcontainer.
/// Mirrors the canonical Weather pattern: the production composition root runs end-to-end
/// (so DI-validation issues, options binding, and middleware ordering are exercised), with
/// only the seam ports swapped in <c>ConfigureTestServices</c>:
/// <see cref="IOutboxWriter"/> -> <see cref="FakeOutboxWriter"/> (captures topic + key + Avro
/// payload without standing up a Schema Registry) and <see cref="IPaymentGateway"/> wrapped in
/// a <see cref="CountingPaymentGateway"/> over the live <see cref="StubPaymentGateway"/>
/// (the deterministic <c>.99</c> -> decline rule keeps firing, only the call counts are
/// observed).
/// </summary>
/// <remarks>
/// Tests resolve the four saga-command Kafka typed handlers from the host DI graph and invoke
/// <c>Handle(IMessageContext, T)</c> directly with a synthetic
/// <see cref="FakeKafkaMessageContext"/>; the KafkaFlow bus itself is not started (the
/// <c>!IsTesting()</c> guard in <c>Program.cs</c> short-circuits the
/// <c>app.Services.CreateKafkaBus().StartAsync()</c> call so no Kafka container is needed in
/// this BC). End-to-end Avro byte-level fidelity lives in the docker-compose smoke (M9).
/// </remarks>
[DisableWafCache]
public class IntegrationTestFixture : AppFixture<Program>
{
    private readonly PostgreSqlTestContainer _dbContainer = new(
        databaseName: "Payments",
        sqlScriptsMigrationsPath: SolutionPaths.SqlScriptMigrationsDirectoryFor("services/Payments/Payments.Infrastructure"),
        new RespawnerOptions
        {
            SchemasToInclude = [PaymentsDbContext.DefaultSchemaName]
        });

    private FakeOutboxWriter _fakeOutbox = null!;
    private CountingPaymentGateway _gateway = null!;

    /// <summary>
    /// Pinned to 2026-04-27 10:00 UTC so assertions on business timestamps don't depend on
    /// wall-clock time.
    /// </summary>
    public FakeTimeProvider FakeTime { get; } = new(
        new DateTimeOffset(2026, 4, 27, 10, 0, 0, TimeSpan.Zero));

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
            webBuilder.UseSetting("ConnectionStrings:Payments", _dbContainer.ConnectionString);
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
                // Pin time so timestamp assertions are stable.
                services.AddSingleton<TimeProvider>(FakeTime);

                // Replace the real Avro/SchemaRegistry-backed outbox writer with an in-memory
                // fake. Captures topic + key + Avro CLR instance for later assertions without
                // standing up a Schema Registry container.
                _fakeOutbox = new FakeOutboxWriter();
                services.RemoveAll<IOutboxWriter>();
                services.AddSingleton<IOutboxWriter>(_fakeOutbox);

                // Wrap the live IPaymentGateway (StubPaymentGateway from production wiring)
                // in a spy decorator so example-mapping examples can assert the saga-retry
                // short-circuit fired before the gateway port was touched. StubPaymentGateway
                // is internal — Payments.Infrastructure exposes InternalsVisibleTo on this
                // assembly.
                services.RemoveAll<IPaymentGateway>();
                services.AddSingleton<IPaymentGateway>(sp =>
                {
                    _gateway = new CountingPaymentGateway(
                        new StubPaymentGateway(sp.GetRequiredService<TimeProvider>()));
                    return _gateway;
                });
            });
    }

    /// <summary>
    /// Creates a per-test DI scope. Caller disposes.
    /// </summary>
    public IServiceScope CreateScope() => Services.CreateScope();

    /// <summary>Wipes every table in the Payments schema between tests.</summary>
    public Task ResetFixtureStateAsync() => _dbContainer.CleanDataAsync();

    /// <summary>
    /// Resolves the singleton <see cref="FakeOutboxWriter"/> so individual tests can
    /// <c>Clear()</c> captured messages or assert on them after driving a handler.
    /// </summary>
    public FakeOutboxWriter GetFakeOutbox() =>
        (FakeOutboxWriter)Services.GetRequiredService<IOutboxWriter>();

    /// <summary>
    /// Resolves the singleton spy decorator over <see cref="StubPaymentGateway"/> so individual
    /// tests can <c>Reset()</c> the call counters between phases or assert that a specific
    /// gateway method was (or was not) invoked.
    /// </summary>
    public CountingPaymentGateway GetGateway() =>
        (CountingPaymentGateway)Services.GetRequiredService<IPaymentGateway>();

    protected override async ValueTask TearDownAsync()
    {
        await _dbContainer.DisposeAsync();
    }
}
