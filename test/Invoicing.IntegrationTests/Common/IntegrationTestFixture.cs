using EntityFramework.Exceptions.PostgreSQL;
using Invoicing.Application.Common.Data;
using Invoicing.Infrastructure.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace Invoicing.IntegrationTests.Common;

/// <summary>
/// Spins a throwaway Postgres container per collection and wires the M5
/// persistence slice: <see cref="InvoicingDbContext"/> and the
/// <see cref="IInvoicingDbContext"/> port binding. The two allocator
/// adapters are NOT registered in DI here — they take a
/// <see cref="TimeProvider"/> dependency, and tests need fresh per-test
/// fakes (the shared <c>FakeTimeProvider</c> rejects backward time moves
/// across xUnit's non-deterministic test ordering). Tests instantiate the
/// adapter directly with a per-test fake clock plus the scoped DbContext.
/// </summary>
/// <remarks>
/// The fixture deliberately bypasses
/// <c>InfrastructureDependencyInjection.AddInvoicingInfrastructure</c> — that
/// extension also wires Azurite + QuestPDF (M3 / M4) which are exercised by
/// their own dedicated fixtures. Pulling them in here would couple every
/// allocator test to those external dependencies for no benefit.
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

        await using var migrationScope = _rootServices.CreateAsyncScope();
        var dbContext = migrationScope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        await dbContext.Database.MigrateAsync(TestContext.Current.CancellationToken);
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
