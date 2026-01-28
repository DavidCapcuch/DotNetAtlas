namespace DotNetAtlas.CQS.Observability;

/// <summary>
/// Common trace tags used by CQS behaviors.
/// </summary>
internal static class TraceTags
{
    // Domain error tags
    public const string DomainError = "domain.error";
    public const string DomainErrorCount = "domain.error.count";

    // Error event tags
    public const string ErrorType = "error.type";
    public const string ErrorCode = "error.code";
    public const string ErrorMessage = "error.message";

    // Exception tags
    public const string ExceptionCritical = "exception.critical";
    public const string ExceptionCode = "exception.code";

    // Event names
    public const string ErrorEvent = "Error";
    public const string DomainErrorEvent = "DomainError";
}
