namespace DotNetAtlas.Domain.Common;

public interface IAuditableEntity
{
    DateTime CreatedUtc { get; set; }

    DateTime LastModifiedUtc { get; set; }
}
