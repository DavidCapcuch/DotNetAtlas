using DotNetAtlas.Domain.Feedback.Events;
using Riok.Mapperly.Abstractions;
using Weather.Feedback;

namespace DotNetAtlas.Application.WeatherFeedback.ChangeFeedback;

[Mapper]
public static partial class ChangeFeedbackMapper
{
    [MapperRequiredMapping(RequiredMappingStrategy.Target)]
    [MapProperty(nameof(FeedbackChangedDomainEvent.OldText.Text), nameof(FeedbackChangedEvent.OldText))]
    [MapProperty(nameof(FeedbackChangedDomainEvent.NewText.Text), nameof(FeedbackChangedEvent.NewText))]
    [MapProperty(nameof(FeedbackChangedDomainEvent.OldRating.Value), nameof(FeedbackChangedEvent.OldRating))]
    [MapProperty(nameof(FeedbackChangedDomainEvent.NewRating.Value), nameof(FeedbackChangedEvent.NewRating))]
    public static partial FeedbackChangedEvent ToFeedbackChangedIntegrationEvent(this FeedbackChangedDomainEvent source);

    [UserMapping]
    private static DateTime DateTimeOffsetToDateTime(DateTimeOffset t) => t.UtcDateTime;
}
