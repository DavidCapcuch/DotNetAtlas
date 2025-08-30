using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using DotNetAtlas.Api.Common.Authentication;

namespace DotNetAtlas.Api.FunctionalTests.Base
{
    /// <summary>
    /// In appsettings.Testing, issuer validation is turned off,
    /// else IDM would need to be permanently running for logins etc.
    /// </summary>
    public class FakeTokenCreator
    {
        public static string GetAdminUserToken()
        {
            var userName = "dev@dotnetatlas.com";
            var roles = new[] {Roles.DEVELOPER};

            return CreateToken(userName, roles);
        }

        public static string GetNormalUserToken()
        {
            var userName = "pleb@dotnetatlas.com";
            var roles = new string[] { };

            return CreateToken(userName, roles);
        }

        private static string CreateToken(string userName, string[] roles)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, userName),
                new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString())
            };

            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var token = new JwtSecurityToken(
                issuer: "NOT CHECKED IN TESTING",
                audience: "NOT CHECKED IN TESTING",
                expires: DateTime.UtcNow.AddHours(1),
                signingCredentials: null,
                claims: claims);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}