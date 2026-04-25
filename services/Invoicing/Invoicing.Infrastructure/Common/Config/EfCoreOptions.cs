using System.ComponentModel.DataAnnotations;

namespace Invoicing.Infrastructure.Common.Config;

/// <summary>
/// EF Core runtime knobs bound from the <c>EfCore</c> configuration section.
/// </summary>
/// <remarks>
/// Note that NpgsqlRetryingExecutionStrategy is deliberately NOT enabled on
/// this BC's DbContext (see <c>PersistenceDependencyInjection</c>): the
/// gap-free allocator (ADR-0018) requires caller-managed transactions, which
/// the retry strategy refuses. Hence no <c>RetryMaxCount</c> /
/// <c>RetryMaxDelaySeconds</c> here — transient-failure recovery is delegated
/// to the outbox-relay retry loop in M7+.
/// </remarks>
public sealed class EfCoreOptions
{
    public const string Section = "EfCore";

    [Required]
    public required bool UseQuerySplitting { get; set; }

    [Required]
    public required bool EnableDetailedErrors { get; set; }
}
