namespace DotNetAtlas.SharedKernel.Base;

/// <summary>
/// Interface for saga state entities that require audit timestamps.
/// Similar to <see cref="IAuditableEntity"/> but with saga-specific naming conventions.
/// </summary>
public interface ISagaAuditableEntity
{
    /// <summary>
    /// UTC timestamp when the saga was created.
    /// </summary>
    DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>
    /// UTC timestamp when the saga was last updated.
    /// </summary>
    DateTimeOffset LastUpdatedAtUtc { get; set; }
}
