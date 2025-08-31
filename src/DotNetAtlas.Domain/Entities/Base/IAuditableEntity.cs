namespace DotNetAtlas.Domain.Entities.Base;

public interface IAuditableEntity
{
    DateTime CreatedUtc { get; set; }

    DateTime LastModifiedUtc { get; set; }
}
