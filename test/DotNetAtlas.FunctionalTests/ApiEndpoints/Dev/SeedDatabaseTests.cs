using System.Net;
using DotNetAtlas.Api.Endpoints.Dev;
using DotNetAtlas.FunctionalTests.Common;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace DotNetAtlas.FunctionalTests.ApiEndpoints.Dev;

[Collection<FeedbackTestCollection>]
public class SeedDatabaseTests : BaseApiTest
{
    public SeedDatabaseTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenNotInRoleDeveloper_ReturnsForbidden()
    {
        // Arrange
        var seedDatabaseCommand = new SeedDatabaseCommand
        {
            NumberOfRecords = 100
        };

        // Act
        var httpResponse =
            await HttpClientRegistry.PlebClient.POSTAsync<SeedDatabaseEndpoint, SeedDatabaseCommand>(
                seedDatabaseCommand);

        // Assert
        httpResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WhenSeedingAsDeveloper_SeedsAndReturnsOk()
    {
        // Arrange
        var currentRecords =
            await DbContext.Feedbacks.CountAsync(TestContext.Current.CancellationToken);
        const int recordsToAdd = 100;
        var expectedRecords = currentRecords + recordsToAdd;
        var seedDatabaseCommand = new SeedDatabaseCommand
        {
            NumberOfRecords = recordsToAdd
        };

        // Act
        var httpResponse =
            await HttpClientRegistry.DevClient.POSTAsync<SeedDatabaseEndpoint, SeedDatabaseCommand>(
                seedDatabaseCommand);

        // Assert
        var totalRecords =
            await DbContext.Feedbacks.CountAsync(TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            totalRecords.Should().Be(expectedRecords);
            httpResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
    }
}
