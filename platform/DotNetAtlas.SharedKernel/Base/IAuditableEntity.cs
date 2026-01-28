namespace DotNetAtlas.SharedKernel.Base;

public interface IAuditableEntity
{
    DateTimeOffset CreatedUtc { get; }

    DateTimeOffset LastModifiedUtc { get; }
}
