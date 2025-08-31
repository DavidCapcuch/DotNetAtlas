namespace DotNetAtlas.Domain.Errors.Base;

public class ForbiddenError : DomainError
{
    public string EntityName { get; }

    public object Id { get; }

    public ForbiddenError(string entityName, object id, string errorCode)
        : base($"You cannot access '{entityName}' with id '{id}'.", errorCode)
    {
        EntityName = entityName;
        Id = id;
    }
}
