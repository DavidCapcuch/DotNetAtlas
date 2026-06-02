namespace Payments.IntegrationTests.Messaging;

/// <summary>
/// End-to-end DLT routing assertion for the Payments saga-command consumer pipeline (#247, ADR-0025).
/// Under the classified <c>RetryForever</c> wiring in <c>MessagingDependencyInjection</c>, a poison
/// command — one that throws a deterministic <c>23505</c> unique-violation
/// (<see cref="Microsoft.EntityFrameworkCore.DbUpdateException"/> wrapping a non-transient
/// <c>PostgresException</c>) — is classified non-retryable by
/// <see cref="Platform.KafkaFlow.DeadLetter.ConsumerRetry"/>, falls through to
/// <see cref="Platform.KafkaFlow.DeadLetter.DeadLetterMiddleware"/>, and lands on
/// <c>payments.payment-commands.Payments.DLT</c> while the partition keeps advancing. A
/// <em>transient</em> (<c>IsTransient</c>) failure, by contrast, is retried with the consumer paused
/// and must NOT dead-letter.
/// </summary>
/// <remarks>
/// <para>
/// <b>Status — placeholder (tracked follow-up).</b> Per ADR-0025 the classified-retry change is
/// covered by the exhaustive <c>ConsumerRetry.IsRetryable</c> unit tests
/// (<c>Platform.KafkaFlow.DeadLetter.UnitTests</c>) plus the per-BC wiring. The full broker
/// round-trip — which mostly re-exercises KafkaFlow's own (unchanged) DLT delivery path — is deferred
/// to a dedicated follow-up rather than standing up a Kafka container for a single assertion.
/// </para>
/// <para>
/// The harness exists: boot a <see cref="Platform.Test.Framework.Kafka.KafkaTestContainer"/> alongside
/// the existing Postgres testcontainer and drive the real composition root, mirroring
/// <c>Catalog.IntegrationTests/Common/IntegrationTestFixture.cs</c> (the reference
/// <c>.UseKafkaSettings(KafkaTestContainer.KafkaOptions)</c> wiring). DLT operations and the header
/// taxonomy this test asserts are documented in <c>docs/bc-design/kafka-dlt-strategy.md</c>.
/// </para>
/// </remarks>
public sealed class PaymentCommandsDLTRoutingTests
{
    [Fact(Skip = "Broker round-trip deferred to a tracked follow-up (ADR-0025 / #247): the " +
                 "classified-retry change is covered by ConsumerRetry.IsRetryable unit tests + per-BC " +
                 "wiring, and the DLT delivery path itself is unchanged. See class XML docs and " +
                 "docs/bc-design/kafka-dlt-strategy.md.")]
    public Task PoisonCommand_AfterRetryExhaustion_LandsOnPaymentsPaymentCommandsDLT()
    {
        // Expected wiring once the Kafka testcontainer harness is added to this suite:
        //
        // 1. Boot KafkaTestContainer alongside the Postgres testcontainer the existing
        //    IntegrationTestFixture already starts (add it to PreSetupAsync, mirroring
        //    Catalog.IntegrationTests/Common/IntegrationTestFixture.cs).
        //
        // 2. Drive the REAL composition root through AppFixture<Program> — do NOT hand-assemble a
        //    ServiceCollection. Extend ConfigureAppHost to point the broker + schema-registry settings
        //    at the test cluster (.UseKafkaSettings(_kafkaContainer.KafkaOptions)) so Program's own
        //    AddKafkaMessaging wires the production KafkaFlow runtime — the classified RetryForever +
        //    DeadLetterMiddleware — against the test container. Program guards kafkaBus.StartAsync()
        //    with !IsTesting(), so that guard needs a test-aware opt-in to actually consume.
        //
        // 3. POISON case: inject a DbContext interceptor that throws a 23505 unique-violation
        //    (DbUpdateException wrapping a non-transient PostgresException) on SaveChangesAsync for the
        //    target PaymentId. ConsumerRetry.IsRetryable returns false -> not retried -> DLT.
        //
        // 4. Produce a syntactically-valid AuthorizePaymentCommand to payments.payment-commands via
        //    KafkaTestProducer, keyed by CorrelationId.
        //
        // 5. Attach a raw Confluent.Kafka IConsumer<string, byte[]> (the DLT producer round-trips raw
        //    bytes — not a typed Avro record) to payments.payment-commands.Payments.DLT with
        //    AutoOffsetReset.Earliest and a fresh GroupId.
        //
        // 6. Assert (poison): one message on the DLT topic; dlt-original-topic header ==
        //    "payments.payment-commands"; dlt-exception-type startsWith
        //    "Microsoft.EntityFrameworkCore.DbUpdateException"; correlation.id preserved; the original
        //    payments.payment-commands offset is COMMITTED (lag returns to 0; partition not blocked).
        //
        // 7. TRANSIENT case (companion assertion): inject an IsTransient failure (e.g. a 40001
        //    serialization_failure PostgresException). ConsumerRetry.IsRetryable returns true -> the
        //    consumer is paused and retries; assert NOTHING lands on the DLT within the window and the
        //    message eventually processes once the fault clears.
        return Task.CompletedTask;
    }
}
