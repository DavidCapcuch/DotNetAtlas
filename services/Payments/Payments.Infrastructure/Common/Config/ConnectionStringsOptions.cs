using System.ComponentModel.DataAnnotations;

namespace Payments.Infrastructure.Common.Config;

/// <summary>
/// Connection strings bound from <c>ConnectionStrings</c> section.
/// Payments owns a single Postgres database (no Redis in v1; the v1
/// <see cref="Payments.Infrastructure.ExternalServices.PaymentGateway.StubPaymentGateway"/>
/// requires no outbound connectivity).
/// </summary>
public sealed class ConnectionStringsOptions
{
    public const string Section = "ConnectionStrings";

    [Required(ErrorMessage = $"{nameof(Payments)} connection string is missing", AllowEmptyStrings = false)]
    public required string Payments { get; set; }
}
