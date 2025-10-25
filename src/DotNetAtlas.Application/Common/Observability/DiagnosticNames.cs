namespace DotNetAtlas.Application.Common.Observability;

public static class DiagnosticNames
{
    public const string City = "request.city";
    public const string CountryCode = "request.country_code";

    public const string FeedbackId = "feedback.id";

    public const string DomainError = "domain.error";
    public const string DomainErrorCount = "domain.error.count";
    public const string DomainErrorDetails = "domain.error.details";

    public const string SignalRGroup = "signalr.group";
    public const string SignalRPayloadLength = "signalr.payload.length";
}
