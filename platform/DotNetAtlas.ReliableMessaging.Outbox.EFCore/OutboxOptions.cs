namespace DotNetAtlas.ReliableMessaging.Outbox.EFCore;

/// <summary>
/// Configuration options for the transactional outbox pattern.
/// Used by <see cref="IOutboxWriter"/> and <see cref="IOutboxWriter{TContext}"/>.
/// </summary>
public sealed class OutboxOptions
{
    /// <summary>
    /// Gets or sets the origin identifier added to outbox message headers.
    /// Typically, the service or application name.
    /// </summary>
    public string? MessageOrigin { get; set; }
}
