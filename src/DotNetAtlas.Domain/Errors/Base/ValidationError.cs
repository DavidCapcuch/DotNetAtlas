namespace DotNetAtlas.Domain.Errors.Base;

public class ValidationError : DomainError
{
    public string PropertyName { get; }

    public ValidationError(string propertyName, string errorMessage, string errorCode)
        : base(errorMessage, errorCode)
    {
        PropertyName = propertyName;
    }
}
