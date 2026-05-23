using System.ComponentModel.DataAnnotations;

namespace Notifications.Infrastructure.Common.Config;

public class ConnectionStringsOptions
{
    public const string Section = "ConnectionStrings";

    [Required(ErrorMessage = $"{nameof(Notifications)} connection string is missing", AllowEmptyStrings = false)]
    public required string Notifications { get; set; }
}
