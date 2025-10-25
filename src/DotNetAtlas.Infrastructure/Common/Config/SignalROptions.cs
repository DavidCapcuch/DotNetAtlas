using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Infrastructure.Common.Config;

/// <summary>
/// SignalR hub configuration options for real-time communication.
/// </summary>
public sealed class SignalROptions
{
    public const string Section = "SignalR";

    /// <summary>
    /// Whether to include detailed error information in responses.
    /// Set to false in production for security.
    /// </summary>
    [Required]
    public required bool EnableDetailedErrors { get; set; }

    /// <summary>
    /// Maximum duration a client connection can be idle before being closed.
    /// Default: 60 seconds (conservative for production).
    /// </summary>
    [Required]
    [Range(10, 300)]
    public required int ClientTimeoutSeconds { get; set; }

    /// <summary>
    /// Interval at which the server sends keep-alive pings to clients.
    /// Should be less than half of ClientTimeoutSeconds.
    /// Default: 15 seconds.
    /// </summary>
    [Required]
    [Range(5, 150)]
    public required int KeepAliveSeconds { get; set; }
}
