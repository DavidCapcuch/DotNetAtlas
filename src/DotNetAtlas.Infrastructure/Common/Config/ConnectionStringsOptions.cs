using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Infrastructure.Common.Config;

public class ConnectionStringsOptions
{
    public const string Section = "ConnectionStrings";

    [Required(ErrorMessage = $"{nameof(Weather)} connection string is missing", AllowEmptyStrings = false)]
    public required string Weather { get; set; }

    [Required(ErrorMessage = $"{nameof(Redis)} connection string is missing", AllowEmptyStrings = false)]
    public required string Redis { get; set; }
}
