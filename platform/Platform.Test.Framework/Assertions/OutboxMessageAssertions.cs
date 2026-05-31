using System.Linq.Expressions;
using AwesomeAssertions;
using AwesomeAssertions.Collections;
using AwesomeAssertions.Primitives;
using Platform.ReliableMessaging.Outbox.Core;

namespace Platform.Test.Framework.Assertions;

/// <summary>
/// Fluent assertions over <see cref="OutboxMessage"/> rows. They replace the repeated
/// <c>om.Type == typeof(T).FullName &amp;&amp; om.KafkaKey == key</c> predicate boilerplate
/// at saga / integration-test call sites. <see cref="OutboxMessage.Type"/> stores the Avro
/// type's full name, so <c>typeof(T).FullName</c> is the value compared against.
/// </summary>
public static class OutboxMessageAssertions
{
    /// <summary>
    /// Asserts the collection contains exactly one <see cref="OutboxMessage"/> whose
    /// <see cref="OutboxMessage.Type"/> equals <c>typeof(T).FullName</c>, optionally also
    /// matching <see cref="OutboxMessage.KafkaKey"/> when a key is supplied.
    /// </summary>
    /// <typeparam name="T">The Avro message type whose full name must be stored in <see cref="OutboxMessage.Type"/>.</typeparam>
    public static AndConstraint<GenericCollectionAssertions<OutboxMessage>> ContainSingleMessageOfType<T>(
        this GenericCollectionAssertions<OutboxMessage> assertions,
        string? kafkaKey = null,
        string because = "",
        params object[] becauseArgs)
        where T : class
    {
        var typeName = typeof(T).FullName;
        Expression<Func<OutboxMessage, bool>> predicate =
            m => m.Type == typeName && (kafkaKey == null || m.KafkaKey == kafkaKey);

        return assertions.ContainSingle(predicate, because, becauseArgs);
    }

    /// <summary>
    /// Asserts the collection contains at least one <see cref="OutboxMessage"/> whose
    /// <see cref="OutboxMessage.Type"/> equals <c>typeof(T).FullName</c>, optionally also
    /// matching <see cref="OutboxMessage.KafkaKey"/> when a key is supplied.
    /// </summary>
    /// <typeparam name="T">The Avro message type whose full name must be stored in <see cref="OutboxMessage.Type"/>.</typeparam>
    public static AndConstraint<GenericCollectionAssertions<OutboxMessage>> ContainMessageOfType<T>(
        this GenericCollectionAssertions<OutboxMessage> assertions,
        string? kafkaKey = null,
        string because = "",
        params object[] becauseArgs)
        where T : class
    {
        var typeName = typeof(T).FullName;
        Expression<Func<OutboxMessage, bool>> predicate =
            m => m.Type == typeName && (kafkaKey == null || m.KafkaKey == kafkaKey);

        return assertions.Contain(predicate, because, becauseArgs);
    }

    /// <summary>
    /// Asserts the <see cref="OutboxMessage.Type"/> string equals <c>typeof(T).FullName</c>.
    /// </summary>
    /// <typeparam name="T">The Avro message type whose full name the value must equal.</typeparam>
    public static AndConstraint<StringAssertions> BeMessageType<T>(
        this StringAssertions assertions,
        string because = "",
        params object[] becauseArgs)
        where T : class
        => assertions.Be(typeof(T).FullName!, because, becauseArgs);
}
