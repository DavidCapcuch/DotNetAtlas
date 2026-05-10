using NetArchTest.Rules;

namespace Invoicing.ArchitectureTests.Infrastructure;

/// <summary>
/// ADR-0017: <c>Azure.Storage.Blobs</c> (and the wider <c>Azure.Storage</c>
/// surface — <c>Azure.Storage.Blobs.Models</c>, <c>Azure.Storage.Sas</c>) is
/// allowed only behind the <c>IBlobStore</c> seam. Domain and Application must
/// stay vendor-neutral so swapping to a different blob backend (or stubbing in
/// tests) does not require touching either layer.
/// </summary>
public sealed class BlobStorageContainmentTests : BaseTest
{
    [Fact]
    public void Domain_ShouldNotHaveDependencyOn_AzureStorage()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOnAny("Azure.Storage")
            .GetResult();
        result.FailingTypes.Should().BeEmpty(
            "Domain must remain free of vendor SDKs — invoice/credit-note aggregates " +
            "describe fiscal records, not blob plumbing");
    }

    [Fact]
    public void Application_ShouldNotHaveDependencyOn_AzureStorage()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOnAny("Azure.Storage")
            .GetResult();
        result.FailingTypes.Should().BeEmpty(
            "Application uses the IBlobStore abstraction (Invoicing.Application.Blobs) — " +
            "Azure.Storage.* belongs to the AzureBlobStore adapter in Invoicing.Infrastructure.Blobs only");
    }
}
