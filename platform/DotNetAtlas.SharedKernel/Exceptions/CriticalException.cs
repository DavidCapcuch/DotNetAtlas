namespace DotNetAtlas.SharedKernel.Exceptions;

/// <summary>
/// Base exception for critical errors indicating bugs or invalid system states.
/// These represent conditions that should never occur in a correctly functioning system.
/// </summary>
public abstract class CriticalException : Exception
{
    /// <summary>
    /// Error code identifying the type of critical error.
    /// </summary>
    public string ErrorCode { get; }

    protected CriticalException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    protected CriticalException(string errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}
