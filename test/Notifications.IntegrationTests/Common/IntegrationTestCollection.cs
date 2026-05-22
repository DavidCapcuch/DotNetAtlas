namespace Notifications.IntegrationTests.Common;

/// <summary>
/// xUnit v3 collection definition scoping <see cref="IntegrationTestFixture"/>
/// — one Postgres container shared across all integration tests in the
/// <c>Notifications-Integration</c> collection, fresh per run.
/// </summary>
[CollectionDefinition(nameof(IntegrationTestCollection))]
public sealed class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>;
