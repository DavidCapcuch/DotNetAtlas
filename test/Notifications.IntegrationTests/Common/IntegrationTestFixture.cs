using EntityFramework.Exceptions.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Notifications.Application.Common.Data;
using Notifications.Application.Email;
using Notifications.Infrastructure.AuthorizePayment;
using Notifications.Infrastructure.Common.Config;
using Notifications.Infrastructure.Common.Persistence.Database;
using Notifications.Infrastructure.Email;
using Notifications.Infrastructure.SendEmailNotification;
using NSubstitute;
using Platform.ReliableMessaging.Outbox.EFCore;
using Testcontainers.PostgreSql;

namespace Notifications.IntegrationTests.Common;

/// <summary>
/// xUnit fixture spinning up a throwaway Postgres container per collection and wiring the
/// Notifications persistence slice (<see cref="NotificationDbContext"/>) together with the
/// Kafka typed handlers registered as Scoped (matching KafkaFlow's
/// <c>WithHandlerLifetime(InstanceLifetime.Scoped)</c>).
/// </summary>
/// <remarks>
/// The transactional outbox (<see cref="ITransactionalOutbox{TContext}"/>) is replaced with
/// an NSubstitute stub so tests can assert on outbox calls without standing up a Confluent
/// Schema Registry container. Blob storage is not used in the Notifications BC so no
/// Azurite container is started. Sequential container startup follows the CLAUDE.md guideline
/// to avoid named-pipe races on Windows.
/// </remarks>
public sealed class IntegrationTestFixture : IAsyncLifetime
{
    /// <summary>Pinned to 2026-05-22 09:00 UTC so assertions on business timestamps are deterministic.</summary>
    public static readonly DateTimeOffset FixedFakeNow =
        new(2026, 05, 22, 09, 00, 00, TimeSpan.Zero);

    private readonly PostgreSqlContainer _pgContainer = new PostgreSqlBuilder("postgres:18.3")
        .WithDatabase("Notifications")
        .WithUsername("postgres")
        .WithPassword("TestingPasswordThatShouldBeInVault123!")
        .WithCleanUp(true)
        .Build();

    private ServiceProvider _rootServices = null!;

    /// <summary>Test-controlled clock pinned to <see cref="FixedFakeNow"/>; resolvable as <see cref="TimeProvider"/>.</summary>
    public FakeTimeProvider FakeTime { get; } = new(FixedFakeNow);

    /// <summary>The shared NSubstitute transactional-outbox stub. Tests assert on its received calls.</summary>
    public ITransactionalOutbox<INotificationDbContext> OutboxSubstitute { get; } =
        Substitute.For<ITransactionalOutbox<INotificationDbContext>>();

    public async ValueTask InitializeAsync()
    {
        // Sequential startup per CLAUDE.md note on Windows named-pipe races.
        await _pgContainer.StartAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();

        services.AddLogging(b => b.AddDebug().SetMinimumLevel(LogLevel.Warning));

        // Minimal in-memory IConfiguration satisfies AddOptionsWithValidateOnStart bindings
        // inside the Notifications composition root (TopicsOptions).
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Topics:NotificationsCommands"] = "notifications.payment-commands",
                ["Topics:Payments"] = "notifications.payments",
                ["Topics:DltTopicSuffix"] = ".Notifications.DLT",
                ["Topics:EmailCommands"] = "notifications.email-commands",
                ["Topics:EmailEvents"] = "notifications.email-events",
            })
            .Build();
        services.AddSingleton<IConfiguration>(config);

        services.AddSingleton<TimeProvider>(FakeTime);

        services.AddDbContext<NotificationDbContext>((_, options) => options
            .UseNpgsql(_pgContainer.GetConnectionString(), npg => npg
                .MigrationsHistoryTable("__EFMigrationsHistory", NotificationDbContext.DefaultSchemaName))
            .UseSnakeCaseNamingConvention()
            .UseExceptionProcessor());

        services.AddScoped<INotificationDbContext>(sp => sp.GetRequiredService<NotificationDbContext>());

        // Stubbed transactional outbox: handlers resolve this stub; tests assert on the
        // stub's received AddOutboxMessage calls. Skipping AddOutbox(...) avoids wiring a
        // schema-registry container in tests.
        services.AddSingleton(OutboxSubstitute);

        // Email collaborators — MockEmailGateway is the production no-op; use a fresh stub
        // so tests can configure call behaviour per scenario.
        services.AddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();
        services.AddScoped<IEmailGateway, MockEmailGateway>();

        // TopicsOptions: bound from the in-memory config above.
        services.AddOptions<TopicsOptions>()
            .BindConfiguration(TopicsOptions.Section)
            .ValidateDataAnnotations();

        // Register the Kafka typed handler classes as Scoped (matches the production wiring in
        // MessagingDependencyInjection.cs). Tests resolve these and invoke Handle(...) directly
        // with a FakeKafkaMessageContext — no KafkaFlow middleware stack needed.
        services.AddScoped<AuthorizePaymentCommandKafkaHandler>();
        services.AddScoped<SendEmailNotificationCommandKafkaHandler>();

        _rootServices = services.BuildServiceProvider();

        // Materialise the schema once per fixture lifetime via EnsureCreatedAsync
        // (mirrors Invoicing's fixture; no EF migrations needed for test isolation).
        await using var setupScope = _rootServices.CreateAsyncScope();
        var dbContext = setupScope.ServiceProvider.GetRequiredService<NotificationDbContext>();
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
}

