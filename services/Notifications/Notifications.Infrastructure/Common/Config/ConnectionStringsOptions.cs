using System.ComponentModel.DataAnnotations;

namespace Notifications.Infrastructure.Common.Config;

public class ConnectionStringsOptions
{
    public const string Section = "ConnectionStrings";

    [Required(ErrorMessage = $"{nameof(Payment)} connection string is missing", AllowEmptyStrings = false)]
    public required string Payment { get; set; }
}
