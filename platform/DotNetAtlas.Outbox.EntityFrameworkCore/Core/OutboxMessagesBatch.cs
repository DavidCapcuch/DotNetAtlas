using System.Collections;

namespace DotNetAtlas.Outbox.EntityFrameworkCore.Core;

/// <summary>
/// Represents the extracted data from an aggregate root.
/// </summary>
public record OutboxMessagesBatch(string? KafkaKey, IEnumerable DomainEvents);
