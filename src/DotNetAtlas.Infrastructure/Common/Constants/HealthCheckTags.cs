namespace DotNetAtlas.Infrastructure.Common.Constants;

public static class HealthCheckTags
{
    /// <summary>
    /// Tag for database-related health checks.
    /// </summary>
    public const string DatabaseTag = "database";

    /// <summary>
    /// Tag for API-related health checks.
    /// </summary>
    public const string ApiTag = "api";

    /// <summary>
    /// Tag for messaging-related health checks.
    /// </summary>
    public const string MessagingTag = "messaging";
}
