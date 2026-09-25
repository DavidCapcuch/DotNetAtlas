using System.Globalization;
using FastEndpoints.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Notifications.Application.Common.Data;
using Notifications.Application.Dispatch;
using Notifications.Infrastructure.Persistence.Database;
using Notifications.IntegrationTests.Common.TestClientInfrastructure;
using NSubstitute;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.Test.Framework;
using Platform.Test.Framework.Auth;
using Platform.Test.Framework.Database;
using Platform.Test.Framework.Kafka;
using Respawn;
using Serilog;
using Serilog.Sinks.XUnit.Injectable;
using Serilog.Sinks.XUnit.Injectable.Abstract;
using Serilog.Sinks.XUnit.Injectable.Extensions;

namespace Notifications.IntegrationTests.Common;

internal sealed class IntegrationTestCollection : TestCollection<IntegrationTestFixture>;

/// <summary>
/// The single Notifications integration fixture: one real <c>Program.cs</c> host on Postgres +
/// Mailpit Testcontainers, shared by the whole <see cref="IntegrationTestCollection"/>. Every
/// entrance runs against it — the bell transport via <see cref="SignalRClientFactory"/>, and the
/// Kafka handler and Hangfire dispatch jobs resolved from a DI scope off
/// <see cref="AppFixture{TProgram}.Services"/>.
/// </summary>
/// <remarks>
/// <para>
/// Program.cs's <c>!IsTesting()</c> guard skips both the KafkaFlow cluster boot and the Hangfire
/// processing server: the typed Kafka handlers and job classes stay registered (tests resolve them
/// from DI) but no consumer opens a broker connection and no job fires out of band.
/// </para>
/// <para>
/// <c>INotificationBroadcaster</c> is deliberately not substituted: the bell's observable outcome is
/// a message arriving at a connected client, and a substitute would reduce that to a mock interaction.
/// </para>
/// <para>
/// Booting through <see cref="AppFixture{TProgram}"/> means every
/// <c>AddOptionsWithValidateOnStart</c> chain in the production composition root runs during
/// fixture initialisation — drift between <c>[Required]</c> IOptions properties and appsettings
/// keys fails at test setup instead of at first container start.
/// </para>
/// <para>
/// Per ADR-0015 the host's <c>TimeProvider.System</c> singleton is left in place — a shared
/// <c>FakeTimeProvider</c> singleton leaks between tests because <c>SetUtcNow</c> cannot move
/// backwards. Tests that need deterministic time construct <c>FakeTimeProvider</c> locally and
/// inject it into a directly-constructed SUT.
/// </para>
/// </remarks>
// No [DisableWafCache]: FastEndpoints caches the WebApplicationFactory per derived fixture type, so a
// second instance of THIS type — e.g. a second TestCollection<IntegrationTestFixture> — would reuse the
// first instance's host instead of booting against its own containers and signer. Add the attribute
// back if that ever happens; a different AppFixture<Program> subclass gets its own host and is safe.
public class IntegrationTestFixture : AppFixture<Program>
{
    private readonly PostgreSqlTestContainer _dbContainer = new(
        databaseName: "Notifications",
        sqlScriptsMigrationsPath: SolutionPaths.SqlScriptMigrationsDirectoryFor("services/Notifications/Notifications.Infrastructure"),
        new RespawnerOptions
        {
            SchemasToInclude = [NotificationsDbContext.DefaultSchemaName]
        });

    private readonly MailpitTestContainer _mailpit = new();

    // Matches Notifications.Api appsettings.json Authentication:JwtBearer:...:ValidAudience.
    // JwtBearerTestExtensions asserts these match (loud on drift).
    private readonly FakeTokenSigner _signer = new(audience: "notifications-service");

    /// <summary>Mints the bearer tokens bell clients connect with.</summary>
    public FakeTokenCreator TokenCreator { get; private set; } = null!;

    /// <summary>NSubstitute transactional-outbox stub. Tests assert on its <c>Received</c> AddOutboxMessage calls.</summary>
    public ITransactionalOutbox<INotificationsDbContext> OutboxSubstitute { get; } =
        Substitute.For<ITransactionalOutbox<INotificationsDbContext>>();

    /// <summary>
    /// Replaces the Hangfire enqueuer the fan-out handler writes to; drain it to run the recorded jobs.
    /// </summary>
    internal RecordingChannelDispatchEnqueuer DispatchEnqueuer { get; } = new();

    /// <summary>Mailpit SMTP sink the email dispatcher delivers to; assert captured mail via its REST API.</summary>
    public MailpitTestContainer Mailpit => _mailpit;

    protected override async ValueTask PreSetupAsync()
    {
        // Start sequentially: concurrent Docker.DotNet InspectContainerAsync calls over the
        // Windows named pipe interleave on the shared ChunkedReadStream and intermittently
        // raise "Invalid chunk header encountered".
        await _dbContainer.StartAsync();
        await _mailpit.StartAsync();
    }

    protected override ValueTask SetupAsync()
    {
        TokenCreator = new FakeTokenCreator(_signer);
        return ValueTask.CompletedTask;
    }

    protected override IHost ConfigureAppHost(IHostBuilder a)
    {
        a.ConfigureWebHost(webBuilder =>
        {
            webBuilder
                .UseSetting("ConnectionStrings:Notifications", _dbContainer.ConnectionString)
                // Point the email channel's SMTP transport at the Mailpit testcontainer.
                .UseSetting("Smtp:Host", _mailpit.SmtpHost)
                .UseSetting("Smtp:Port", _mailpit.SmtpPort.ToString(CultureInfo.InvariantCulture))
                .UseUnreachableKafkaSettings();
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
                // Swap the production Avro+SchemaRegistry-backed ITransactionalOutbox for an
                // NSubstitute stub. Tests assert on received AddOutboxMessage calls — production
                // wiring requires a live Schema Registry which we don't stand up here.
                services.Replace(ServiceDescriptor.Singleton<ITransactionalOutbox<INotificationsDbContext>>(OutboxSubstitute));

                // No Hangfire server runs in the test host, so record the fan-out's enqueues instead.
                services.Replace(ServiceDescriptor.Singleton<IChannelDispatchEnqueuer>(DispatchEnqueuer));

                // Trust the test signer's RSA key while keeping every TokenValidationParameters flag
                // at its production default of TRUE; asserts the BC's ValidAudience == signer audience.
                services.ConfigureJwtBearerForTests(_signer);
            });
    }

    /// <summary>Creates a per-test DI scope; caller disposes (supports <c>await using</c>).</summary>
    public AsyncServiceScope CreateScope() => Services.CreateAsyncScope();

    /// <summary>Wipes the Notifications schema, the captured Mailpit mail and the recorded outbox calls and enqueues between tests.</summary>
    public async Task ResetFixtureStateAsync()
    {
        OutboxSubstitute.ClearReceivedCalls();
        DispatchEnqueuer.Clear();
        await _dbContainer.CleanDataAsync();
        await _mailpit.DeleteAllAsync();
    }

    protected override async ValueTask TearDownAsync()
    {
        _signer.Dispose();
        await _dbContainer.DisposeAsync();
        await _mailpit.DisposeAsync();
    }
}
