using FluentResults;

namespace DotNetAtlas.Domain.Errors.Base;

public abstract class DomainError : Error
{
    public string ErrorCode { get; }

    protected DomainError(string errorMessage, string errorCode)
        : base(errorMessage)
    {
        ErrorCode = errorCode;
    }
}
