namespace EShop.BFF.IntegrationTests.Common;

/// <summary>Shared base: exposes the fixture and flushes redis-cache after each test for isolation.</summary>
public abstract class BaseBasketPageTest(BasketPageTestFixture fixture) : IAsyncLifetime
{
    protected BasketPageTestFixture Fixture { get; } = fixture;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await Fixture.ResetFixtureStateAsync();
}
