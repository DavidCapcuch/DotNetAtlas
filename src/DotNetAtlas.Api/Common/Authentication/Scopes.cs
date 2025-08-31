using Ardalis.SmartEnum;

namespace DotNetAtlas.Api.Common.Authentication;

/// <summary>
/// Bunch of scopes.
/// </summary>
/// <remarks>
/// Be aware that scopes are case-sensitive, and you have to specify them exactly as they were defined and specified at Rights.
/// </remarks>
internal class Scopes : SmartEnum<Scopes>
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
