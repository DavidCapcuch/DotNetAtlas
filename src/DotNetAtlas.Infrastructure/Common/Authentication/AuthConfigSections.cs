namespace DotNetAtlas.Infrastructure.Common.Authentication;

public static class AuthConfigSections
{
    public const string JwtBearerConfigSection = "Authentication:JwtBearer";
    public const string OAuthConfigSection = "Authentication:OAuth";
    public const string OidcConfigSection = "Authentication:Oidc";
    public const string CookieConfigSection = "Authentication:Cookie";
}
