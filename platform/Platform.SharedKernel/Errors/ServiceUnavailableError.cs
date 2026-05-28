namespace Platform.SharedKernel.Errors;

/// <summary>
/// A <see cref="DomainError"/> indicating that a required resource or dependency
/// is currently unavailable — covers both upstream-dependency failures
/// (e.g. an external service is down) and recoverable internal-state issues
/// that warrant a client retry. Maps to HTTP 503 Service Unavailable when
/// surfaced through the API layer.
/// </summary>
public class ServiceUnavailableError : DomainError
{
    public string ResourceName { get; }

    public ServiceUnavailableError(string resourceName, string message, string errorCode)
        : base($"'{resourceName}' is unavailable: {message}", errorCode)
    {
        ResourceName = resourceName;
    }
}
