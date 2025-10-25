namespace DotNetAtlas.Domain.Common.Errors;

public class ConflictError : DomainError
{
    public string EntityName { get; }

    public ConflictError(string entityName, string message, string errorCode)
        : base($"Conflict occurred on '{entityName}': {message}", errorCode)
    {
        EntityName = entityName;
    }
}
