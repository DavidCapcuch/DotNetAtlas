namespace DotNetAtlas.Domain.Entities.Base
{
    public interface IAuditableEntity
    {
        public DateTime CreatedUtc { get; set; }
        public DateTime LastModifiedUtc { get; set; }
    }
}