using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace DotNetAtlas.Api.Endpoints.Auth;

public sealed class LoginEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        // Starts OIDC authentication flow, redirect to IDM server, therefore, GET.
        Get("login");
        Group<AuthGroup>();
        AllowAnonymous();
        Description(b => b.Produces(302)
            .WithSummary("Redirects to IDM server for authentication."));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated == false)
        {
            await HttpContext.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties
            {
                RedirectUri = "/"
            });
            await HttpContext.Response.CompleteAsync();

            return;
        }

        await Send.RedirectAsync("/");
    }
}
