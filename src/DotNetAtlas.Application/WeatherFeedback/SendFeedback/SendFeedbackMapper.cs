using DotNetAtlas.Domain.Feedback.Events;
using Riok.Mapperly.Abstractions;
using Weather.Feedback;

namespace DotNetAtlas.Application.WeatherFeedback.SendFeedback;

[Mapper]
public static partial class SendFeedbackMapper
{
    [MapperRequiredMapping(RequiredMappingStrategy.Target)]
    [MapProperty(nameof(FeedbackCreatedDomainEvent.Text.Text), nameof(FeedbackCreatedEvent.Text))]
    [MapProperty(nameof(FeedbackCreatedDomainEvent.Rating.Value), nameof(FeedbackCreatedEvent.Rating))]
    public static partial FeedbackCreatedEvent ToFeedbackCreatedIntegrationEvent(this FeedbackCreatedDomainEvent source);

    [UserMapping]
    private static DateTime DateTimeOffsetToDateTime(DateTimeOffset t) => t.UtcDateTime;
}
