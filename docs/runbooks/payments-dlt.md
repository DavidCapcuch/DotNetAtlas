# Runbook — `payments.commands.Payments.DLT`

> **Owners:** Payments BC oncall.
> **Trigger:** non-zero message count on the dead-letter topic
> `payments.commands.Payments.DLT`.
> **Severity:** SEV-2 (money-handling consumer is unable to process a command).
> **Created:** wave1-followup #247 (replaces `RetryForever` with `RetrySimple(TryTimes: 8) + AddDeadLetter`).

---

## 1. What landing here means

A saga-emitted Avro command on `payments.commands` survived 8 bounded retry
attempts (`TryTimes(8)`, backoff `500ms → 1s → 2s → 5s` then 5s plateau) inside
the Payments consumer pipeline and threw one of `DbUpdateException`,
`NpgsqlException`, or `TimeoutException` every time. The
`Platform.KafkaFlow.DeadLetter.DeadLetterMiddleware` then re-produced the raw
message bytes to `payments.commands.Payments.DLT` with the original key
preserved and these forensic headers attached (`Platform.KafkaFlow.DeadLetter.DltHeaders`):

| Header | Meaning |
|---|---|
| `dlt-original-topic` | Always `payments.commands` here. |
| `dlt-original-partition` | Partition the message originally landed on. |
| `dlt-original-offset` | Offset of the message on `payments.commands`. |
| `dlt-exception-type` | FQ name of the terminal exception (e.g. `Microsoft.EntityFrameworkCore.DbUpdateException`). |
| `dlt-exception-message` | The exception's `Message` property. |
| `dlt-exception-stack-trace` | Full `Exception.ToString()` (stack + inner exception chain). |
| `correlation.id` | (Inherited) ADR-0008 saga correlation id of the failing command. |
| `messageId` | (Inherited) idempotency anchor for the outbox / inbox. |

Crucially: **the original `payments.commands` offset was committed** — the
partition is no longer blocked. Saga-side retries triggered by the lack of a
downstream response will pile up new messages on `payments.commands` until ops
intervene.

> **Not-a-bug case.** A schema-evolution mismatch (consumer can't deserialize)
> will land here too: the Avro deserializer throws before any of the three
> retried-on exception types fire, so the dead-letter middleware catches the
> deserialization exception directly. Header `dlt-exception-type` distinguishes
> the case.

---

## 2. Query path — what landed?

Boot the local stack (or attach to a real environment broker) and read the
DLT topic with header rendering on:

```bash
docker compose --profile full up -d kafka
docker compose exec kafka \
  kafka-console-consumer \
    --bootstrap-server kafka:9092 \
    --topic payments.commands.Payments.DLT \
    --from-beginning \
    --property print.headers=true \
    --property print.key=true \
    --property print.offset=true \
    --property print.partition=true \
    --timeout-ms 10000
```

Count messages quickly:

```bash
docker compose exec kafka \
  kafka-run-class kafka.tools.GetOffsetShell \
    --broker-list kafka:9092 \
    --topic payments.commands.Payments.DLT
# Summed end-offsets across partitions = total messages ever produced.
```

For ad-hoc operator queries, point [Redpanda Console / kafka-ui] at
`payments.commands.Payments.DLT` — the headers are already structured.

---

## 3. Triage decision tree

1. **Read the first message.** Note `dlt-exception-type`, `correlation.id`,
   `messageId`.
2. **Classify the failure.**
   - `DbUpdateException` mentioning a constraint name or `duplicate key`:
     application bug — the command would never have applied. Skip (Section 4.B).
   - `DbUpdateException` with `connection refused` / `timed out` /
     `relation does not exist`: infrastructure regression — fix the underlying
     DB issue first, then replay (Section 4.A).
   - `NpgsqlException` `08006` / `57P03`: Postgres availability — pager DB
     oncall, then replay.
   - `TimeoutException`: the DbContext's command timeout fired. Likely a long
     query holding a lock; investigate via `pg_stat_activity`.
   - `Confluent.SchemaRegistry.Serdes.*` / `Avro.AvroException`: schema-registry
     compatibility break. Don't replay until the producer-side schema is
     reverted or the consumer is upgraded.
3. **Check the saga.** The saga sitting behind this command is timing out and
   re-emitting. Look at the corresponding `checkout.sagas` state row — if it
   shows ≥3 retries against the same `messageId`, expect duplicates on the
   DLT. Treat the cluster as one logical incident.

---

## 4. Recovery

### 4.A — Replay (after root-cause fixed)

Re-produce the DLT messages to `payments.commands` so the consumer can pick
them up again. The Payments inbox-dedup table (`payments.inbox_messages`)
short-circuits already-applied messages by `messageId`, so replay is safe even
if some saga retries already succeeded.

```bash
# 1. Drain the DLT into a file (records the messageIds so you can audit).
docker compose exec kafka kafka-console-consumer \
  --bootstrap-server kafka:9092 \
  --topic payments.commands.Payments.DLT \
  --from-beginning \
  --property print.headers=true \
  --max-messages 100 \
  > /tmp/dlt-snapshot.txt

# 2. Replay via mirror-maker or the kcat one-shot below. Strip DLT headers
#    so the original message looks fresh to the consumer:
kcat -b localhost:9094 \
  -t payments.commands.Payments.DLT -C -q -e -o beginning \
  -f '%k\t%s\n' \
| while IFS=$'\t' read -r key value; do
    kcat -b localhost:9094 -t payments.commands -P -k "$key" <<< "$value"
done
```

The saga's exactly-once-effect contract is upheld by the inbox row — a
message that successfully applied before the DLT roundtrip lands as an
idempotent no-op.

### 4.B — Skip (poison-pill that can never apply)

If the failing command is genuinely un-applicable (e.g. references an
`OrderId` that was rolled back; a schema-registry rejection from a
deprecated subject), document the `messageId`s in the incident ticket and
**do not replay**. Drain the DLT into the incident log and emit a
`PaymentFailedEvent`-equivalent compensation manually if the saga doesn't
self-recover within the configured timeout.

> If you find yourself skipping >2 messages in a 24h window, escalate — the
> producer-side bug is reproducing.

---

## 5. Escalation

| Step | Who | When |
|---|---|---|
| Open SEV-2 incident ticket | Triage engineer | Immediately on first DLT message that isn't a known schema-evolution test. |
| Page Payments oncall | Triage engineer | If DLT count > 0 sustained for > 5 min. |
| Page Platform oncall (Kafka / DB) | Payments oncall | If failure class is infra (Section 3 step 2 → `NpgsqlException 08006`, `TimeoutException`, schema-registry outage). |
| Customer-comms (refund ledger desync) | Payments oncall + Support lead | If DLT messages survive > 1h unactioned — refunds may show inconsistency between saga state and Payments ledger. |

---

## 6. Forward-prevention

- Watch the `payments.commands.Payments.DLT` topic in Grafana with a non-zero
  alert at the 5-minute window.
- After every incident, run the `dotnet test test/Payments.IntegrationTests/Messaging/PaymentCommandsDLTRoutingTests`
  smoke locally — if the test surfaces a regression in the retry-then-DLT
  pipeline before the next incident, the runbook trigger is preserved.
- Bounded-retry count (`TryTimes(8)`) is tuned for the worst-case 4-step
  backoff plan. Increasing past 8 only helps if the underlying failure is
  transient over a longer window than ~30s — at that point a DB-availability
  pager is the right tool, not deeper consumer retries.
