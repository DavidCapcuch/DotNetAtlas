using DotNetAtlas.Domain.Entities.Weather.Feedback.Events;
using Riok.Mapperly.Abstractions;
using Weather.Feedback;

namespace DotNetAtlas.Infrastructure.Messaging.Kafka.DomainToAvroMappings;

[Mapper]
public static partial class FeedbackEventsToAvroMapper
{
    [MapperRequiredMapping(RequiredMappingStrategy.Target)]
    [MapValue(nameof(FeedbackCreatedEvent.EventId), Use = nameof(GenerateEventId))]
    public static partial FeedbackCreatedEvent ToFeedbackCreatedEvent(this FeedbackCreatedDomainEvent source);

    [MapperRequiredMapping(RequiredMappingStrategy.Target)]
    [MapValue(nameof(FeedbackChangedEvent.EventId), Use = nameof(GenerateEventId))]
    public static partial FeedbackChangedEvent ToFeedbackChangedEvent(this FeedbackChangedDomainEvent source);

    private static Guid GenerateEventId() => Guid.CreateVersion7();

    [UserMapping]
    private static DateTime DateTimeOffsetToDateTime(DateTimeOffset t) => t.UtcDateTime;
}
