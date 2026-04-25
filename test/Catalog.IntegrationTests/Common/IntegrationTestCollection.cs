namespace Catalog.IntegrationTests.Common;

/// <summary>
/// xUnit v3 collection definition — every test decorated with
/// <c>[Collection(nameof(IntegrationTestCollection))]</c> shares one Postgres
/// container but stays isolated from unrelated collections.
/// </summary>
[CollectionDefinition(nameof(IntegrationTestCollection))]
public sealed class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>;
