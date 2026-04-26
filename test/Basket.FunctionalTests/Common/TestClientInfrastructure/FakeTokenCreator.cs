using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Basket.FunctionalTests.Common.TestClientInfrastructure;

public static class FakeTokenCreator
{
    /// <summary>
    /// Creates an unsigned JWT for the test host. Mirrors
    /// <c>test/Weather.FunctionalTests/Common/TestClientInfrastructure/FakeTokenCreator.cs</c>
    /// with two simplifications: only two <see cref="ClientType"/>s (Basket has no
    /// developer-only routes) and an explicit JWT <c>sub</c> claim so
    /// <c>Basket.Api.Common.Extensions.ClaimsPrincipalExtensions.GetUserIdFromSubClaim</c>
    /// can read the user id without relying on .NET's
    /// <see cref="ClaimTypes.NameIdentifier"/> aliasing.
    /// </summary>
    public static string CreateUserToken(ClientType clientType, Guid? userId = null)
    {
        return clientType switch
        {
            ClientType.RegularUser => CreateToken(userId ?? Guid.CreateVersion7()),
            ClientType.NonAuth => string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(clientType)),
        };
    }

    private static string CreateToken(Guid userId)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, $"user-{userId}@dotnetatlas.test"),
        };

        var handler = new JsonWebTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = "NOT CHECKED IN TESTING",
            Audience = "NOT CHECKED IN TESTING",
            Expires = DateTime.UtcNow.AddHours(1),
            Subject = new ClaimsIdentity(claims),
        };

        return handler.CreateToken(descriptor);
    }
}
