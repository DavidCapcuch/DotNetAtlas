using System.Net;
using AwesomeAssertions;
using AwesomeAssertions.Execution;
using DotNetAtlas.Api.Endpoints.Dev;
using DotNetAtlas.Api.FunctionalTests.Base;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace DotNetAtlas.Api.FunctionalTests.ApiEndpoints.Dev;

[Collection<CollectionA>]
public class SeedDatabaseTests : BaseApiTest
{
    public SeedDatabaseTests(ApiTestFixture app, ITestOutputHelper testOutputHelper)
        : base(app, testOutputHelper)
    {
    }

    [Fact]
    public async Task WhenNotInRoleDeveloper_ReturnsForbidden()
    {
        // Arrange and Act
        var httpResponse =
            await PlebClient.POSTAsync<SeedDatabaseEndpoint, SeedDatabaseCommand>(
                new SeedDatabaseCommand
                {
                    NumberOfRecords = 100
                });

        // Assert
        httpResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WhenSeedingAsDeveloper_SeedsAndReturnsOk()
    {
        // Arrange
        var currentRecords =
            await DbContext.WeatherFeedbacks.CountAsync(TestContext.Current.CancellationToken);
        const int recordsToAdd = 100;
        var expectedRecords = currentRecords + recordsToAdd;

        // Act
        var httpResponse =
            await DevClient.POSTAsync<SeedDatabaseEndpoint, SeedDatabaseCommand>(
                new SeedDatabaseCommand
                {
                    NumberOfRecords = recordsToAdd
                });

        // Assert
        var totalRecords =
            await DbContext.WeatherFeedbacks.CountAsync(TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            totalRecords.Should().Be(expectedRecords);
            httpResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
    }
}
