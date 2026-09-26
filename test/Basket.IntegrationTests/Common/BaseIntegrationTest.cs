using Basket.Application.Abstractions;
using Basket.Domain.Baskets.ValueObjects;
using Basket.Infrastructure.Persistence.Database;
using Basket.IntegrationTests.Common.TestClientInfrastructure;
using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Platform.Test.Framework.Tracing;
using Serilog.Sinks.XUnit.Injectable.Abstract;

namespace Basket.IntegrationTests.Common;

/// <summary>
/// Base for every Basket integration test. Exposes the HTTP edge
/// (<see cref="HttpClientRegistry"/>) for slice tests that enter through the endpoint, the
/// Catalog ACL substitute for stubbing the upstream, and a per-test DI scope with its
/// <see cref="BasketDbContext"/> for arranging fixture state and asserting persisted outcomes.
/// State is reset between tests via the fixture's <c>ResetFixtureStateAsync</c>.
/// </summary>
public abstract class BaseIntegrationTest : IAsyncLifetime
{
    private readonly TestCaseTracer _testCaseTracer;
    private readonly Func<Task> _resetFixtureStateAsync;

    protected IntegrationTestFixture Fixture { get; }

    protected IServiceScope Scope { get; }

    protected BasketDbContext DbContext { get; }

    protected HttpClientRegistry<Program> HttpClientRegistry { get; }

    protected IProductCatalogQueryPort Catalog => Fixture.Catalog;

    /// <summary>Asserting on a snapshot field? Pass it via <see cref="BasketTestData.Snapshot"/>.</summary>
    protected void StubCatalogProduct(Guid productId, ProductSnapshot? snapshot = null) =>
        Catalog.GetProductSnapshotAsync(productId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(snapshot ?? BasketTestData.Snapshot()));

    protected BaseIntegrationTest(IntegrationTestFixture app)
    {
        Fixture = app;
        var outputSink = app.Services.GetRequiredService<IInjectableTestOutputSink>();
        outputSink.Inject(TestContext.Current.TestOutputHelper!);

        _resetFixtureStateAsync = app.ResetFixtureStateAsync;
        Scope = app.Services.CreateScope();
        DbContext = Scope.ServiceProvider.GetRequiredService<BasketDbContext>();

        // In local Jaeger, you will see a trace operation with the name of each test method that you can examine.
        // Inspired by https://github.com/martinjt/unittest-with-otel/tree/main
        _testCaseTracer = new TestCaseTracer(
            Scope.ServiceProvider,
            TestContext.Current.TestMethod!.MethodName,
            TestContext.Current.TestCase!.UniqueID,
            testType: "integration");

        HttpClientRegistry = app.HttpClientRegistry;
        HttpClientRegistry.SetTraceParent(_testCaseTracer.TraceParent);
    }

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (TestContext.Current.TestState?.Result == TestResult.Failed)
        {
            _testCaseTracer.RecordTestFailure(
                TestContext.Current.TestState.ExceptionMessages);
        }

        _testCaseTracer.LogTestTraceLocalJaegerLink();

        _testCaseTracer.Dispose();
        await _resetFixtureStateAsync();
        Scope.Dispose();
    }
}
