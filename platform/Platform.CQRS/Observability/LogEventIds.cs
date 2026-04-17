namespace Platform.CQRS.Observability;

/// <summary>
/// Event IDs for CQRS behavior logging.
/// </summary>
internal static class LogEventIds
{
    /// <summary>
    /// Event IDs for command handling (1000-1999).
    /// </summary>
    internal static class Command
    {
        internal const int Handling = 1000;
        internal const int Handled = 1001;
        internal const int DomainError = 1002;
        internal const int Exception = 1003;
        internal const int CriticalException = 1004;
    }

    /// <summary>
    /// Event IDs for query handling (2000-2999).
    /// </summary>
    internal static class Query
    {
        internal const int Handling = 2000;
        internal const int Handled = 2001;
        internal const int DomainError = 2002;
        internal const int Exception = 2003;
        internal const int CriticalException = 2004;
    }
}
