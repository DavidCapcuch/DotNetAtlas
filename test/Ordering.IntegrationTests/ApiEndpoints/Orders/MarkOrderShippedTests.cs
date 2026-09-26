using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Ordering.Api.Common.Authorization;
using Ordering.Api.Endpoints.Orders.MarkOrderShipped;
using Ordering.Domain.Orders;
using Ordering.IntegrationTests.Common;
using Ordering.IntegrationTests.Common.TestClientInfrastructure;
using Platform.Test.Framework.Auth;

namespace Ordering.IntegrationTests.ApiEndpoints.Orders;

[Collection<IntegrationTestCollection>]
public class MarkOrderShippedTests : BaseIntegrationTest
{
    public MarkOrderShippedTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task WhenNotAuthenticated_ReturnsUnauthorized()
    {
        var response = await HttpClientRegistry.NonAuthClient
            .POSTAsync<MarkOrderShippedEndpoint, MarkOrderShippedRequest>(
                new MarkOrderShippedRequest
                {
                    OrderId = Guid.CreateVersion7(),
                    Carrier = "DHL",
                    TrackingNumber = "TRK-1",
                });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task WhenAuthenticatedAsBuyer_ReturnsForbidden()
    {
        var response = await HttpClientRegistry.BuyerClient
            .POSTAsync<MarkOrderShippedEndpoint, MarkOrderShippedRequest>(
                new MarkOrderShippedRequest
                {
                    OrderId = Guid.CreateVersion7(),
                    Carrier = "DHL",
                    TrackingNumber = "TRK-1",
                });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WhenOrderMissing_ReturnsNotFound()
    {
        var (response, _) = await HttpClientRegistry.AdminClient
            .POSTAsync<MarkOrderShippedEndpoint, MarkOrderShippedRequest, ProblemDetails>(
                new MarkOrderShippedRequest
                {
                    OrderId = Guid.CreateVersion7(),
                    Carrier = "DHL",
                    TrackingNumber = "TRK-1",
                });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "critical-path")]
    public async Task WhenOrderConfirmed_ReturnsNoContentAndStatusShipped()
    {
        var seed = new OrderSeed(DbContext, TimeProvider.System);
        var order = await seed.CreateConfirmedOrderAsync(TestUsers.BuyerId, cancellationToken: TestContext.Current.CancellationToken);

        var response = await HttpClientRegistry.AdminClient
            .POSTAsync<MarkOrderShippedEndpoint, MarkOrderShippedRequest>(
                new MarkOrderShippedRequest
                {
                    OrderId = order.Id,
                    Carrier = "DHL",
                    TrackingNumber = "TRK-42",
                });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);

            var refreshed = await DbContext.Orders.AsNoTracking()
                .SingleAsync(o => o.Id == order.Id, TestContext.Current.CancellationToken);
            refreshed.Status.Should().Be(OrderStatus.Shipped);
            refreshed.Shipment!.Carrier.Should().Be("DHL");
            refreshed.Shipment.TrackingNumber.Should().Be("TRK-42");
        }
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task WhenTokenCarriesOnlyKeycloakFlatRolesClaim_AdminAuthSucceeds()
    {
        // Pins the platform's Keycloak-roles auth contract (#234 close-out).
        //
        // Production Keycloak admin tokens carry roles in the flat "roles"
        // array claim only (src/keycloak/realm-export.json "roles-flat" —
        // oidc-usermodel-realm-role-mapper). They do NOT carry the
        // ClaimTypes.Role URI claim that ASP.NET's IsInRole reads by default.
        //
        // Admin auth still works because:
        //   1. JwtBearerOptions.MapInboundClaims defaults to TRUE in
        //      Microsoft.AspNetCore.Authentication.JwtBearer
        //      (initialized from JwtSecurityTokenHandler.DefaultMapInboundClaims;
        //      see aspnetcore JwtBearerOptions.cs).
        //   2. The InboundClaimTypeMap in Microsoft.IdentityModel.JsonWebTokens
        //      includes {"roles" → ClaimTypes.Role} and {"role" → ClaimTypes.Role}.
        //   3. So during validation the "roles" claim is rewritten to
        //      ClaimTypes.Role on the principal, and IsInRole(Roles.Admin)
        //      returns true against the default RoleClaimType.
        //
        // This test mints a token whose ONLY role claim is the flat "roles"
        // claim, bypassing FakeTokenCreator (which uses ClaimTypes.Role for
        // historical reasons), and asserts that an admin-only endpoint
        // accepts it. If a future change ever disables MapInboundClaims,
        // sets RoleClaimType away from ClaimTypes.Role, or otherwise breaks
        // the auto-mapping in platform/Platform.ServiceDefaults/Auth/
        // JwtBearerConfigurator.cs, this test fails loudly — admin auth
        // across every BC consuming AddPlatformJwtBearer would break in
        // production at the same time.
        var seed = new OrderSeed(DbContext, TimeProvider.System);
        var order = await seed.CreateConfirmedOrderAsync(TestUsers.BuyerId, cancellationToken: TestContext.Current.CancellationToken);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, TestUsers.AdminId.ToString()),
            new Claim(ClaimTypes.Name, "admin@dotnetatlas.com"),
            new Claim("roles", Roles.Admin),
        };
        var token = FakeTokenBuilder.SignToken(Fixture.Signer, claims);

        using var client = Fixture.CreateClient(c =>
            c.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token));

        var response = await client.POSTAsync<MarkOrderShippedEndpoint, MarkOrderShippedRequest>(
            new MarkOrderShippedRequest
            {
                OrderId = order.Id,
                Carrier = "DHL",
                TrackingNumber = "TRK-PIN-234",
            });

        response.StatusCode.Should().Be(
            HttpStatusCode.NoContent,
            "a Keycloak-shape token with only the flat \"roles\" claim must satisfy OrderingAdmin — a 403 here means JwtBearer inbound claim mapping was disabled or RoleClaimType was overridden in JwtBearerConfigurator, which would break admin auth across every BC");
    }
}
