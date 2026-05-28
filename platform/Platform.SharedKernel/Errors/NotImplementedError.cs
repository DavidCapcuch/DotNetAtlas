namespace Platform.SharedKernel.Errors;

/// <summary>
/// A <see cref="DomainError"/> indicating that a feature has been recognised but
/// is intentionally not implemented in the current API version (e.g. partial
/// refunds deferred to a future release). Maps to HTTP 501 Not Implemented when
/// surfaced through the API layer.
/// </summary>
public class NotImplementedError : DomainError
{
    public string FeatureName { get; }

    public NotImplementedError(string featureName, string message, string errorCode)
        : base($"'{featureName}' is not implemented: {message}", errorCode)
    {
        FeatureName = featureName;
    }
}
