using System.Net;
using Catalog.Api.Endpoints.Categories.CreateCategory;
using Catalog.Api.Endpoints.Categories.GetCategoryTree;
using Catalog.Application.Categories.GetCategoryTree;
using Catalog.FunctionalTests.Common;
using FastEndpoints;

namespace Catalog.FunctionalTests.CrossCutting;

/// <summary>
/// Verifies the ADR-0010 scope policy pair (<c>catalog.read</c> / <c>catalog.write</c>) is
/// honoured. Read endpoints accept either scope; write endpoints reject the read-only token.
/// </summary>
[Collection<FunctionalTestCollection>]
public class JwtScopeAuthorizationTests : BaseApiTest
{
    public JwtScopeAuthorizationTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenWriteTokenHitsReadEndpoint_Returns200()
    {
        // Write scope implies read — admin tokens can call query endpoints.
        var (response, _) = await HttpClientRegistry.WriteClient
            .GETAsync<GetCategoryTreeEndpoint, GetCategoryTreeRequest, GetCategoryTreeResponse>(
                new GetCategoryTreeRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WhenReadTokenHitsWriteEndpoint_Returns403()
    {
        var response = await HttpClientRegistry.ReadClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest>(
                CatalogTestData.ValidCreateCategoryRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WhenWriteScopeButNotAdmin_Returns403()
    {
        // Defense-in-depth: WritePolicy requires the admin role AND the catalog.write
        // scope. A token holding the scope but lacking the role must still be rejected —
        // this pins the role half so it can't be silently dropped.
        var response = await HttpClientRegistry.WriteScopeNoAdminClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest>(
                CatalogTestData.ValidCreateCategoryRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WhenNonAuthHitsWriteEndpoint_Returns401()
    {
        var response = await HttpClientRegistry.NonAuthClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest>(
                CatalogTestData.ValidCreateCategoryRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WhenNonAuthHitsReadEndpoint_Returns401()
    {
        var (response, _) = await HttpClientRegistry.NonAuthClient
            .GETAsync<GetCategoryTreeEndpoint, GetCategoryTreeRequest, GetCategoryTreeResponse>(
                new GetCategoryTreeRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
