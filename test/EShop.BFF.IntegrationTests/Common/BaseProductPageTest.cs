namespace EShop.BFF.IntegrationTests.Common;

/// <summary>Shared base: exposes the fixture and flushes redis-cache after each test for isolation.</summary>
public abstract class BaseProductPageTest(ProductPageTestFixture fixture) : IAsyncLifetime
{
    protected ProductPageTestFixture Fixture { get; } = fixture;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await Fixture.ResetFixtureStateAsync();
}
