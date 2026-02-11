using System.ComponentModel.DataAnnotations;

namespace Ordering.Infrastructure.Common.Config;

public class ConnectionStringsOptions
{
    public const string Section = "ConnectionStrings";

    [Required(ErrorMessage = $"{nameof(Ordering)} connection string is missing", AllowEmptyStrings = false)]
    public required string Ordering { get; set; }
}
