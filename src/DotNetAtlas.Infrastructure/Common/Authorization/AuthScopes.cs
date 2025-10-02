using Ardalis.SmartEnum;

namespace DotNetAtlas.Infrastructure.Common.Authorization;

public class AuthScopes : SmartEnum<AuthScopes>
{
    public static readonly AuthScopes OpenId = new("openid", "OpenID", 0, "OpenID.");

    public static readonly AuthScopes Profile = new("profile", "Profile", 1, "Profile.");

    public static readonly AuthScopes Email = new("email", "Email", 2, "Email.");

    public static readonly AuthScopes OfflineAccess =
        new("offline_access", "Refresh Token", 3, "Generate refresh token.");

    public string DisplayName { get; }

    public string Description { get; }

    private AuthScopes(string name, string displayName, int value, string description)
        : base(name, value)
    {
        DisplayName = displayName;
        Description = description;
    }
}
