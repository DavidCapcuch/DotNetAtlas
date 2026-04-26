using EntityFramework.Exceptions.PostgreSQL;
using Invoicing.Application.Common.Data;
using Invoicing.Infrastructure.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace Invoicing.IntegrationTests.Common;

/// <summary>
/// Spins a throwaway Postgres container per collection and wires the M5/M6
/// persistence slice: <see cref="InvoicingDbContext"/>, the
/// <see cref="IInvoicingDbContext"/> port binding, and (M6) schema for the
/// <c>pending_invoices</c> + <c>pending_credit_notes</c> projection tables
/// plus the platform <c>inbox_messages</c> table. Tests construct the
/// projection KafkaFlow handler classes directly with per-test
/// <c>FakeTimeProvider</c> instances + an NSubstitute <c>IMessageContext</c>;
/// the inbox middleware is exercised by Platform.KafkaFlow.Inbox.EFCore's
/// own tests, not here. The two allocator adapters (M5) are also instantiated
/// directly so tests can inject test-controlled clocks.
/// </summary>
/// <remarks>
/// Schema is materialised via <see cref="DatabaseFacade.EnsureCreatedAsync"/>
/// rather than EF migrations — per CLAUDE.md the user generates production
/// migrations deterministically; tests derive the schema from the EF model
/// so the fixture stays self-contained and a M6 (or later) test run does
/// not block on a manually-authored migration. Mirrors Catalog M4's choice.
/// </remarks>
public sealed class IntegrationTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pgContainer = new PostgreSqlBuilder("postgres:18.3")
        .WithDatabase("Invoicing")
        .WithUsername("postgres")
        .WithPassword("TestingPasswordThatShouldBeInVault123!")
        .WithCleanUp(true)
        .Build();

    private ServiceProvider _rootServices = null!;

    public async ValueTask InitializeAsync()
    {
        await _pgContainer.StartAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();

        services.AddLogging(b => b.AddDebug().SetMinimumLevel(LogLevel.Warning));

        services.AddDbContext<InvoicingDbContext>(options => options
            .UseNpgsql(_pgContainer.GetConnectionString(), npg => npg
                .MigrationsHistoryTable("__EFMigrationsHistory", InvoicingDbContext.DefaultSchemaName))
            .UseSnakeCaseNamingConvention()
            .UseExceptionProcessor());

        services.AddScoped<IInvoicingDbContext>(sp => sp.GetRequiredService<InvoicingDbContext>());

        _rootServices = services.BuildServiceProvider();

        await using var setupScope = _rootServices.CreateAsyncScope();
        var dbContext = setupScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        await dbContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Creates a per-test DI scope; caller disposes (supports <c>await using</c>).</summary>
    public AsyncServiceScope CreateScope() => _rootServices.CreateAsyncScope();

    /// <summary>Connection string for tests that bypass the DbContext (e.g. raw SQL pre-staging).</summary>
    public string ConnectionString => _pgContainer.GetConnectionString();

    public async ValueTask DisposeAsync()
    {
        if (_rootServices is not null)
        {
            await _rootServices.DisposeAsync();
        }

        await _pgContainer.DisposeAsync();
    }
}

/// <summary>
/// xUnit v3 collection definition scoping <see cref="IntegrationTestFixture"/>
/// — one Postgres container shared across all integration tests in the
/// <c>Invoicing-Integration</c> collection, fresh per run.
/// </summary>
[CollectionDefinition(nameof(IntegrationTestCollection))]
public sealed class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>;
