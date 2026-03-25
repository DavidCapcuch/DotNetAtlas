namespace Platform.CQS.Observability;

/// <summary>
/// Common metric tags used by CQS behaviors.
/// </summary>
internal static class MetricsTags
{
    // Operation name tags
    public const string CommandName = "command_name";
    public const string QueryName = "query_name";

    // Status tags
    public const string Status = "status";
    public const string StatusSuccess = "success";
    public const string StatusFailed = "failed";
    public const string StatusException = "exception";

    // Error tags
    public const string ErrorType = "error_type";
    public const string ErrorCode = "error_code";

    // Exception tags
    public const string ExceptionType = "exception_type";
    public const string IsCritical = "is_critical";
}
