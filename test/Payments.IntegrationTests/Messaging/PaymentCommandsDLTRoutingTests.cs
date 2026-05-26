namespace Payments.IntegrationTests.Messaging;

/// <summary>
/// End-to-end DLT routing assertion for the Payments saga-command consumer pipeline (#247).
/// The bounded-retry replacement (<c>RetrySimple(TryTimes: 8)</c> in
/// <c>MessagingDependencyInjection</c>) is load-bearing: when a poison command throws
/// <see cref="Microsoft.EntityFrameworkCore.DbUpdateException"/> on every attempt, the exception
/// must bubble out of the retry middleware, into <see cref="Platform.KafkaFlow.DeadLetter.DeadLetterMiddleware"/>,
/// and land on <c>payments.payment-commands.Payments.DLT</c> within timeout — proving the partition
/// keeps advancing instead of being blocked by the indefinite <c>RetryForever</c> the wire-up
/// replaces.
/// </summary>
/// <remarks>
/// <para>
/// <b>Status — placeholder.</b> The existing <see cref="Common.IntegrationTestFixture"/> exercises
/// the consumer pipeline by invoking the typed Kafka handlers directly via a
/// <see cref="Common.FakeKafkaMessageContext"/>; the production KafkaFlow runtime (including
/// the <c>RetrySimple</c> middleware and the <c>DeadLetterMiddleware</c>) is bypassed. Asserting
/// DLT routing requires booting a real <see cref="Platform.Test.Framework.Kafka.KafkaTestContainer"/>
/// alongside the existing Postgres testcontainer, wiring the BC composition root's
/// <c>AddKafkaMessaging</c> against the test container's bootstrap, producing the poison Avro
/// command, and attaching a <see cref="Platform.Test.Framework.Kafka.KafkaTestConsumer{TValue}"/>
/// (or raw <c>Confluent.Kafka</c> consumer) to <c>payments.payment-commands.Payments.DLT</c>. That
/// infrastructure is a follow-up since neither this BC nor any wave1 BC integration suite has
/// it today (Weather is the only test project that wires the full
/// <c>UseKafkaSettings(KafkaTestContainer.KafkaOptions)</c> shape).
/// </para>
/// <para>
/// <b>Why the placeholder still earns its keep.</b> The file pins the file path the runbook
/// (<c>docs/runbooks/payments-dlt.md § 6</c>) references and the closeout points at; future
/// hardening lands here without renaming. The expected assertion shape is captured in the
/// commented-out body below so the next contributor doesn't re-derive it.
/// </para>
/// </remarks>
public sealed class PaymentCommandsDLTRoutingTests
{
    [Fact(Skip = "Requires KafkaTestContainer + SchemaRegistry harness (follow-up). " +
                 "See class XML docs. Bounded-retry behaviour is enforced by unit-level " +
                 "ripgrep + arch tests; full DLT roundtrip needs a real Kafka pipeline.")]
    public Task PoisonCommand_AfterRetryExhaustion_LandsOnPaymentsPaymentCommandsDLT()
    {
        // Expected wiring once the Kafka testcontainer harness exists:
        //
        // 1. Boot KafkaTestContainer + SchemaRegistryTestContainer alongside the Postgres
        //    testcontainer the existing IntegrationTestFixture already starts.
        //
        // 2. Configure the production Payments composition root against the test cluster:
        //      services.UseKafkaSettings(_kafkaContainer.KafkaOptions)
        //      services.AddApplication();
        //      services.AddKafkaMessaging(configuration);  // includes the new RetrySimple(8) + DLT.
        //
        // 3. Inject a DbContext interceptor that throws DbUpdateException on every
        //    SaveChangesAsync against the inbox/aggregate row for the target PaymentId — surviving
        //    the 8 backoff steps configured by RetrySimple's WithTimeBetweenTriesPlan.
        //
        // 4. Produce a syntactically-valid AuthorizePaymentCommand to payments.payment-commands via
        //    KafkaTestProducer, keyed by CorrelationId.
        //
        // 5. Attach a raw Confluent.Kafka IConsumer<string, byte[]> (the DLT producer round-trips
        //    raw bytes — not a typed Avro record) to payments.payment-commands.Payments.DLT with
        //    AutoOffsetReset.Earliest and a fresh GroupId.
        //
        // 6. Within ~30s (8 retries × 5s + slack), assert:
        //      - One message on the DLT topic.
        //      - dlt-original-topic header == "payments.payment-commands".
        //      - dlt-exception-type header startsWith "Microsoft.EntityFrameworkCore.DbUpdateException".
        //      - correlation.id header is preserved end-to-end.
        //      - Original Kafka offset on payments.payment-commands has been COMMITTED (consumer group
        //        lag returns to 0; partition is not blocked).
        return Task.CompletedTask;
    }
}
