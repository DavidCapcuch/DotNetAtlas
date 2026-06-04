using System.ComponentModel.DataAnnotations;

namespace Notifications.Infrastructure.Common.Config;

/// <summary>
/// SMTP transport settings for the email channel. Points at Mailpit locally
/// (<c>core</c> compose profile, port 1025); a real relay is a deployment concern. ADR-0032 § 6.
/// </summary>
public sealed class SmtpOptions
{
    public const string Section = "Smtp";

    [Required]
    [Length(1, 253)]
    public required string Host { get; set; }

    [Range(1, 65535)]
    public required int Port { get; set; }

    [Required]
    [EmailAddress]
    public required string FromAddress { get; set; }

    [Required]
    [Length(1, 200)]
    public required string FromName { get; set; }
}
