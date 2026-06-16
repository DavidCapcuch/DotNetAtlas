using System.Net;
using EShop.BFF.Api.Composition;
using EShop.BFF.Api.Responses;
using FastEndpoints;
using FluentResults;
using Platform.Api.Extensions;
using Platform.SharedKernel.Errors;

namespace EShop.BFF.Api.Endpoints.HomePage;

/// <summary>
/// <c>GET /api/v1/bff/home-page</c> (bff.md § 3.4). Public. Returns the featured products + category tree
/// + stock overlay, served from the eagerly-refreshed <c>home-page:v1</c> FusionCache. Catalog search
/// gates the page (down + no stale cache → 503); the category tree and Inventory overlay degrade it
/// (<c>categoryTree</c> / availability nulled, <c>X-BFF-PartialData</c> header, 200) rather than fail it.
/// </summary>
internal sealed class GetHomePageEndpoint : EndpointWithoutRequest<HomePageResponse>
{
    private readonly HomePageProvider _homePage;

    public GetHomePageEndpoint(HomePageProvider homePage)
    {
        _homePage = homePage;
    }

    public override void Configure()
    {
        Get("home-page");
        Version(1);
        Group<BffGroup>();
        AllowAnonymous();
        Summary(s => s.Summary =
            "Public home page: featured products + category tree + stock overlay, edge-cached (bff.md § 3.4).");
        Description(b =>
        {
            b.Produces<HomePageResponse>((int)HttpStatusCode.OK);
            b.Produces((int)HttpStatusCode.ServiceUnavailable);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        HomePageResponse page;
        try
        {
            page = await _homePage.GetOrComposeAsync(ct);
        }
        catch (UpstreamUnavailableException)
        {
            // Catalog search is down and no stale page was cached to fail-safe to.
            await Send.SendErrorResponseAsync(
                Result.Fail(new ServiceUnavailableError(
                    "home-page",
                    "an upstream dependency is unavailable",
                    "Bff.HomePage.Unavailable")),
                ct);
            return;
        }

        SignalPartialData(page);

        await Send.OkAsync(page, ct);
    }

    // bff.md § 3.4 failure table: a null category tree / stock overlay each surface as X-BFF-PartialData.
    private void SignalPartialData(HomePageResponse page)
    {
        var partial = new List<string>(capacity: 2);
        if (page.CategoryTree is null)
        {
            partial.Add("categories");
        }

        if (page.StockHighlights is null)
        {
            partial.Add("inventory");
        }

        if (partial.Count > 0)
        {
            HttpContext.Response.Headers["X-BFF-PartialData"] = string.Join(", ", partial);
        }
    }
}
