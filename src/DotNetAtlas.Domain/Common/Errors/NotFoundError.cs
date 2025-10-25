namespace DotNetAtlas.Domain.Common.Errors;

public class NotFoundError : DomainError
{
    public string EntityName { get; }

    public object Id { get; }

    public NotFoundError(string entityName, object id, string errorCode)
        : base($"'{entityName}' '{id}' not found.", errorCode)
    {
        EntityName = entityName;
        Id = id;
    }
}
