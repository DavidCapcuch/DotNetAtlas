namespace DotNetAtlas.SharedKernel.Exceptions;

/// <summary>
/// Thrown when data integrity is violated due to bugs.
/// This should NEVER happen in production. If it does, it indicates a bug
/// in the calling code (e.g., payment service sent invalid data).
/// </summary>
public class DataIntegrityException : CriticalException
{
    public DataIntegrityException(string errorCode, string message)
        : base(errorCode, message)
    {
    }

    public DataIntegrityException(string errorCode, string message, Exception innerException)
        : base(errorCode, message, innerException)
    {
    }
}
