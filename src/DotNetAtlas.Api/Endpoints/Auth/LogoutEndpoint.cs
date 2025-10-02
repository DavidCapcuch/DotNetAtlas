using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace DotNetAtlas.Api.Endpoints.Auth;

public sealed class LogoutEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("logout");
        Group<AuthGroup>();
        AuthSchemes(CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme);
        Description(b => b.Produces(302)
                .WithSummary("Logs out the user and redirects back to index"));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);
        await HttpContext.Response.CompleteAsync();
    }
}
