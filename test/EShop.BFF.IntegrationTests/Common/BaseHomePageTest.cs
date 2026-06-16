namespace EShop.BFF.IntegrationTests.Common;

/// <summary>Shared base: exposes the fixture and resets WireMock + flushes redis-cache after each test.</summary>
public abstract class BaseHomePageTest(HomePageTestFixture fixture) : IAsyncLifetime
{
    protected HomePageTestFixture Fixture { get; } = fixture;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await Fixture.ResetFixtureStateAsync();
}
