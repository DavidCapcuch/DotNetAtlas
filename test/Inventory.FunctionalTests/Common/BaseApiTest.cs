using Inventory.FunctionalTests.Common.TestClientInfrastructure;
using Inventory.Infrastructure.Persistence.Database;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Sinks.XUnit.Injectable.Abstract;

namespace Inventory.FunctionalTests.Common;

public abstract class BaseApiTest : IAsyncLifetime
{
    private readonly Func<Task> _resetFixtureStateAsync;

    protected IServiceScope Scope { get; }

    protected InventoryDbContext DbContext { get; }

    protected InventoryApiFixture Fixture { get; }

    protected HttpClientRegistry<Program> HttpClientRegistry { get; }

    protected BaseApiTest(InventoryApiFixture app)
    {
        Fixture = app;

        // xUnit v3 makes TestContext.Current ambient inside the test class
        // ctor — relied on for sink injection here. If a future xUnit
        // upgrade tightens that, move the Inject call into InitializeAsync.
        // Matches Basket / Weather / Catalog functional-test bases.
        var outputSink = app.Services.GetRequiredService<IInjectableTestOutputSink>();
        outputSink.Inject(TestContext.Current.TestOutputHelper!);

        _resetFixtureStateAsync = app.ResetFixtureStateAsync;
        Scope = app.Services.CreateScope();
        DbContext = Scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        HttpClientRegistry = app.HttpClientRegistry;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _resetFixtureStateAsync();
        Scope.Dispose();
    }
}
