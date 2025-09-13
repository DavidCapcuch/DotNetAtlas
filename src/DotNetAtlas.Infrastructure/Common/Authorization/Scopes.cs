using Ardalis.SmartEnum;

namespace DotNetAtlas.Infrastructure.Common.Authorization;

public class Scopes : SmartEnum<Scopes>
{
    public static readonly Scopes Email = new("openid", "OpenID", 0, "OpenID.");

    public static readonly Scopes Profile = new("profile", "Profile", 0, "Profile.");

    public static readonly Scopes OpenId = new("email", "Email", 0, "Email.");

    public string DisplayName { get; }

    public string Description { get; }

    private Scopes(string name, string displayName, int value, string description)
        : base(name, value)
    {
        DisplayName = displayName;
        Description = description;
    }
}
