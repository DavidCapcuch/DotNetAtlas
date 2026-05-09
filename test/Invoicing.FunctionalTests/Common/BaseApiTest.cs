using Invoicing.FunctionalTests.Common.TestClientInfrastructure;
using Invoicing.Infrastructure.Persistence.Database;
using Microsoft.Extensions.DependencyInjection;

namespace Invoicing.FunctionalTests.Common;

/// <summary>
/// Base class for Invoicing functional tests. Creates a per-test DI scope so each test
/// gets its own <see cref="InvoicingDbContext"/>, exposes the shared
/// <see cref="HttpClientRegistry{TEntryPoint}"/>, and resets fixture state (Redis flush +
/// table truncation) on dispose so subsequent tests see a clean slate.
/// </summary>
public abstract class BaseApiTest : IAsyncLifetime
{
    private readonly Func<Task> _resetFixtureStateAsync;
    protected IServiceScope Scope { get; }
    protected InvoicingDbContext DbContext { get; }
    protected HttpClientRegistry<Program> HttpClientRegistry { get; }
    protected ApiTestFixture App { get; }

    protected BaseApiTest(ApiTestFixture app)
    {
        App = app;
        _resetFixtureStateAsync = app.ResetFixtureStateAsync;
        Scope = app.Services.CreateScope();
        DbContext = Scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();
        HttpClientRegistry = app.HttpClientRegistry;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _resetFixtureStateAsync();
        Scope.Dispose();
    }
}
