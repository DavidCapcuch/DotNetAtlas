namespace Inventory.IntegrationTests.Common;

/// <summary>
/// Per-test reset hook for the shared <see cref="IntegrationTestFixture"/>:
/// xUnit invokes <see cref="DisposeAsync"/> after every test method, which
/// calls <see cref="IntegrationTestFixture.ResetFixtureStateAsync"/> to wipe
/// the Inventory schema via Respawn. Mirrors
/// <c>Inventory.FunctionalTests/Common/BaseApiTest.cs</c>.
/// </summary>
/// <remarks>
/// Tests still create their own DI scopes (often more than one per method)
/// via <see cref="IntegrationTestFixture.CreateScope"/>; this base does not
/// pre-create a shared scope, only the reset hook.
/// </remarks>
public abstract class BaseIntegrationTest : IAsyncLifetime
{
    private readonly Func<Task> _resetFixtureStateAsync;

    protected IntegrationTestFixture Fixture { get; }

    protected BaseIntegrationTest(IntegrationTestFixture fixture)
    {
        Fixture = fixture;
        _resetFixtureStateAsync = fixture.ResetFixtureStateAsync;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _resetFixtureStateAsync();
    }
}
