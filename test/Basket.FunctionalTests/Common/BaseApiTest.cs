using Basket.Application.Abstractions;
using Basket.FunctionalTests.Common.TestClientInfrastructure;
using Basket.Infrastructure.Persistence.Database;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Sinks.XUnit.Injectable.Abstract;

namespace Basket.FunctionalTests.Common;

public abstract class BaseApiTest : IAsyncLifetime
{
    private readonly Func<Task> _resetFixtureStateAsync;

    protected IServiceScope Scope { get; }

    protected BasketDbContext DbContext { get; }

    protected ApiTestFixture Fixture { get; }

    protected HttpClientRegistry<Program> HttpClientRegistry { get; }

    protected IProductCatalogQueryPort Catalog => Fixture.Catalog;

    protected BaseApiTest(ApiTestFixture app)
    {
        Fixture = app;
        var outputSink = app.Services.GetRequiredService<IInjectableTestOutputSink>();
        outputSink.Inject(TestContext.Current.TestOutputHelper!);

        _resetFixtureStateAsync = app.ResetFixtureStateAsync;
        Scope = app.Services.CreateScope();
        DbContext = Scope.ServiceProvider.GetRequiredService<BasketDbContext>();
        HttpClientRegistry = app.HttpClientRegistry;
    }

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _resetFixtureStateAsync();
        Scope.Dispose();
    }
}
