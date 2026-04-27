using Catalog.FunctionalTests.Common.TestClientInfrastructure;
using Catalog.Infrastructure.Persistence.Database;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using OpenFeature;
using Serilog.Sinks.XUnit.Injectable.Abstract;

namespace Catalog.FunctionalTests.Common;

public abstract class BaseApiTest : IAsyncLifetime
{
    private readonly Func<Task> _resetFixtureStateAsync;

    protected IServiceScope Scope { get; }

    protected CatalogDbContext DbContext { get; }

    protected ApiTestFixture Fixture { get; }

    protected HttpClientRegistry<Program> HttpClientRegistry { get; }

    protected IFeatureClient FeatureClient => Fixture.FeatureClient;

    protected FakeTimeProvider TimeProvider => Fixture.TimeProvider;

    protected BaseApiTest(ApiTestFixture app)
    {
        Fixture = app;
        var outputSink = app.Services.GetRequiredService<IInjectableTestOutputSink>();
        outputSink.Inject(TestContext.Current.TestOutputHelper!);

        _resetFixtureStateAsync = app.ResetFixtureStateAsync;
        Scope = app.Services.CreateScope();
        DbContext = Scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        HttpClientRegistry = app.HttpClientRegistry;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _resetFixtureStateAsync();
        Scope.Dispose();
    }
}
