namespace DotNetAtlas.Application.Common.Observability;

public static class TraceTags
{
    public const string City = "request.city";
    public const string CountryCode = "request.country_code";

    public const string FeedbackId = "feedback.id";

    public const string SignalRGroup = "signalr.group";
    public const string SignalRPayloadLength = "signalr.payload.length";

    public const string UserId = "user.id";
}
