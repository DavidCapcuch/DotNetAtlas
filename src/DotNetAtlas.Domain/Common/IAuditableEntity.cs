namespace DotNetAtlas.Domain.Common;

public interface IAuditableEntity
{
    DateTimeOffset CreatedUtc { get; set; }

    DateTimeOffset LastModifiedUtc { get; set; }
}
