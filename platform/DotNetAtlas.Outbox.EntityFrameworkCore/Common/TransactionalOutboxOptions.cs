namespace DotNetAtlas.Outbox.EntityFrameworkCore.Common;

/// <summary>
/// Configuration options for <see cref="EntityFramework.ITransactionalOutbox"/>.
/// </summary>
public sealed class TransactionalOutboxOptions
{
    /// <summary>
    /// Gets or sets the origin identifier added to outbox message headers.
    /// Typically the service or application name.
    /// </summary>
    public string? MessageOrigin { get; set; }
}
