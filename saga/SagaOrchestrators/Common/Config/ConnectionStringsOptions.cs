using System.ComponentModel.DataAnnotations;

namespace SagaOrchestrators.Common.Config;

/// <summary>
/// Connection strings configuration for saga services.
/// </summary>
public sealed class ConnectionStringsOptions
{
    public const string Section = "ConnectionStrings";

    [Required(ErrorMessage = $"{nameof(Saga)} connection string is missing", AllowEmptyStrings = false)]
    public required string Saga { get; set; }
}
