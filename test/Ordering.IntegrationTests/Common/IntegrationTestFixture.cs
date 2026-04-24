using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Ordering.Application.Common.Data;
using Ordering.Infrastructure.Persistence.Database;
using Ordering.Infrastructure.Persistence.Database.Interceptors;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.Common;
using Testcontainers.PostgreSql;
using Xunit;

namespace Ordering.IntegrationTests.Common;

/// <summary>
/// xUnit fixture spinning up a throwaway Postgres container per collection
/// and wiring the slice of the Ordering stack exercised in M4: the
/// <see cref="OrderingDbContext"/>, its interceptors, and the domain-event
/// dispatcher (with no outbox publishers, so tests don't need a real schema
/// registry). The Kafka saga-command ingress path is tested end-to-end in M7.
/// </summary>
public sealed class IntegrationTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pgContainer = new PostgreSqlBuilder("postgres:18.3")
        .WithDatabase("Ordering")
        .WithUsername("postgres")
        .WithPassword("TestingPasswordThatShouldBeInVault123!")
        .WithCleanUp(true)
        .Build();

    private ServiceProvider _rootServices = null!;

    /// <summary>
    /// Pinned to 2026-04-23 10:00 UTC so assertions on <c>CreatedAtUtc</c>
    /// etc. do not depend on wall-clock time.
    /// </summary>
    public FakeTimeProvider FakeTime { get; } = new(
        new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero));

    public async ValueTask InitializeAsync()
    {
        await _pgContainer.StartAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();

        services.AddLogging(b => b.AddDebug().SetMinimumLevel(LogLevel.Warning));

        services.AddSingleton<TimeProvider>(FakeTime);

        // Domain-event dispatcher with NO registered handlers: interceptor will
        // still pop events from aggregates but no outbox publisher fires. This
        // isolates M4 verification to the persistence slice (DbContext + EF
        // mappings + interceptor plumbing). Outbox publishers are exercised
        // end-to-end in M7 when the schema-registry container is in scope.
        services.AddDomainEventDispatcher();

        services.AddScoped<DispatchDomainEventsInterceptor>();
        services.AddSingleton<UpdateAuditableEntitiesInterceptor>();

        services.AddDbContext<OrderingDbContext>((sp, options) => options
            .UseNpgsql(_pgContainer.GetConnectionString(), npg => npg
                .MigrationsHistoryTable("__EFMigrationsHistory", OrderingDbContext.DefaultSchemaName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                sp.GetRequiredService<UpdateAuditableEntitiesInterceptor>(),
                sp.GetRequiredService<DispatchDomainEventsInterceptor>()));

        services.AddScoped<IOrderingDbContext>(sp => sp.GetRequiredService<OrderingDbContext>());

        _rootServices = services.BuildServiceProvider();

        // Apply EF migrations once per fixture lifetime.
        await using var migrationScope = _rootServices.CreateAsyncScope();
        var dbContext = migrationScope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        await dbContext.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Creates a per-test DI scope. Caller disposes.
    /// </summary>
    public IServiceScope CreateScope() => _rootServices.CreateScope();

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
/// xUnit v3 collection definition — fixtures share one container across all
/// tests in the collection but are isolated from other collections.
/// </summary>
[CollectionDefinition(nameof(IntegrationTestCollection))]
public sealed class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>;
