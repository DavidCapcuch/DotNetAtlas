using Microsoft.Extensions.DependencyInjection;
using Ordering.FunctionalTests.Common.TestClientInfrastructure;
using Ordering.Infrastructure.Persistence.Database;

namespace Ordering.FunctionalTests.Common;

/// <summary>
/// Base class for Ordering functional tests. Creates a per-test DI scope so
/// each test gets its own <see cref="OrderingDbContext"/>, exposes the
/// shared <see cref="HttpClientRegistry{TEntryPoint}"/>, and resets fixture
/// state (Redis flush + table truncation) on dispose so subsequent tests
/// see a clean slate.
/// </summary>
public abstract class BaseApiTest : IAsyncLifetime
{
    private readonly Func<Task> _resetFixtureStateAsync;
    protected IServiceScope Scope { get; }
    protected OrderingDbContext DbContext { get; }
    protected HttpClientRegistry<Program> HttpClientRegistry { get; }
    protected ApiTestFixture App { get; }

    protected BaseApiTest(ApiTestFixture app)
    {
        App = app;
        _resetFixtureStateAsync = app.ResetFixtureStateAsync;
        Scope = app.Services.CreateScope();
        DbContext = Scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        HttpClientRegistry = app.HttpClientRegistry;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _resetFixtureStateAsync();
        Scope.Dispose();
    }
}
