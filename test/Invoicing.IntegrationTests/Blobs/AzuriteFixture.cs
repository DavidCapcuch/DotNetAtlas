using System.Net.Http;
using Azure.Storage.Blobs;
using Invoicing.Application.Blobs;
using Invoicing.Infrastructure.Blobs;
using Microsoft.Extensions.Options;
using Testcontainers.Azurite;
using Xunit;

namespace Invoicing.IntegrationTests.Blobs;

/// <summary>
/// xUnit v3 fixture spinning up a throwaway Azurite container per collection and
/// exposing a ready-to-use <see cref="IBlobStore"/> (+ a raw <see cref="HttpClient"/>
/// for fetching SAS URLs in GET assertions).
/// </summary>
public sealed class AzuriteFixture : IAsyncLifetime
{
    private const string InvoicesContainerName = "invoices-test";

    private readonly AzuriteContainer _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.31.0")
        .WithCommand("--skipApiVersionCheck")
        .WithCleanUp(true)
        .Build();

    public BlobServiceClient ServiceClient { get; private set; } = default!;

    public IBlobStore BlobStore { get; private set; } = default!;

    public HttpClient Http { get; } = new();

    public string ContainerName => InvoicesContainerName;

    public async ValueTask InitializeAsync()
    {
        await _azurite.StartAsync(TestContext.Current.CancellationToken);

        ServiceClient = new BlobServiceClient(_azurite.GetConnectionString());
        await ServiceClient
            .GetBlobContainerClient(InvoicesContainerName)
            .CreateIfNotExistsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var options = Options.Create(new BlobStorageOptions
        {
            ConnectionString = _azurite.GetConnectionString(),
            InvoicesContainerName = InvoicesContainerName,
        });
        BlobStore = new AzureBlobStore(ServiceClient, options);
    }

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await _azurite.DisposeAsync();
    }
}

/// <summary>
/// xUnit v3 collection definition scoping <see cref="AzuriteFixture"/> to the
/// <c>Azurite</c> collection \u2014 one container shared across all tests in the
/// collection, fresh per run.
/// </summary>
[CollectionDefinition(nameof(AzuriteCollection))]
public sealed class AzuriteCollection : ICollectionFixture<AzuriteFixture>;
