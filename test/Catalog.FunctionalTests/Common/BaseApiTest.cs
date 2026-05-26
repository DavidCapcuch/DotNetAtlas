using Catalog.FunctionalTests.Common.TestClientInfrastructure;
using Catalog.Infrastructure.Persistence.Database;
using Microsoft.Extensions.DependencyInjection;
using OpenFeature;
using Platform.Test.Framework.Tracing;
using Serilog.Sinks.XUnit.Injectable.Abstract;

namespace Catalog.FunctionalTests.Common;

public abstract class BaseApiTest : IAsyncLifetime
{
    private readonly Func<Task> _resetFixtureStateAsync;

    protected TestCaseTracer TestCaseTracer { get; }

    protected ApiTestFixture Fixture { get; }

    protected IServiceScope Scope { get; }

    protected CatalogDbContext DbContext { get; }

    protected HttpClientRegistry<Program> HttpClientRegistry { get; }

    protected IFeatureClient FeatureClient => Fixture.FeatureClient;

    protected BaseApiTest(ApiTestFixture app)
    {
        Fixture = app;
        var outputSink = app.Services.GetRequiredService<IInjectableTestOutputSink>();
        outputSink.Inject(TestContext.Current.TestOutputHelper!);

        _resetFixtureStateAsync = app.ResetFixtureStateAsync;
        Scope = app.Services.CreateScope();
        DbContext = Scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        // In local Jaeger, you will see a trace operation with the name of each test method that you can examine.
        // Inspired by https://github.com/martinjt/unittest-with-otel/tree/main
        TestCaseTracer = new TestCaseTracer(
            Scope.ServiceProvider,
            TestContext.Current.TestMethod!.MethodName,
            TestContext.Current.TestCase!.UniqueID,
            testType: "functional");

        HttpClientRegistry = app.HttpClientRegistry;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (TestContext.Current.TestState?.Result == TestResult.Failed)
        {
            TestCaseTracer.RecordTestFailure(
                TestContext.Current.TestState.ExceptionMessages);
        }

        await _resetFixtureStateAsync();
        TestCaseTracer.Dispose();
        Scope.Dispose();
    }
}
