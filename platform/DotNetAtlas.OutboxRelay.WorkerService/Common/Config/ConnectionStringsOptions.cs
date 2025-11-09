using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.OutboxRelay.WorkerService.Common.Config;

public class ConnectionStringsOptions
{
    public const string Section = "ConnectionStrings";

    [Required(ErrorMessage = $"{nameof(Outbox)} connection string is missing", AllowEmptyStrings = false)]
    public required string Outbox { get; set; }
}
