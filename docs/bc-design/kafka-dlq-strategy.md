# Kafka DLQ & Retry Strategy

> Governance for dead-lettering and consumer retries across every eShop topic. Reuses the existing [`Platform.KafkaFlow.DeadLetter`](../../platform/Platform.KafkaFlow.DeadLetter/) middleware — this document specifies **per-topic configuration** and **operational procedures** so consumers are dead-letter-aware from day one.
>
> Terminology: the existing codebase, docker-compose references, and `TopicsOptions.DltTopicSuffix` all use the term **DLT** (Dead-Letter **Topic**). The generic industry term is **DLQ** (Dead-Letter **Queue**). They refer to the same thing in this repo. This document uses **DLT** in file names and topic identifiers (matching the running code) and **DLQ** in prose when discussing strategy.

---

## 1. DLT Naming Convention

`{topic}.DLT` — e.g., `inventory.reservations.DLT`, `ordering.order-commands.DLT`.

The suffix is applied by [`DeadLetterMiddleware`](../../platform/Platform.KafkaFlow.DeadLetter/DeadLetterMiddleware.cs) using the configured `DltTopicSuffix` from each service's `TopicsOptions` (see [`src/Weather.Application/Common/Messaging/TopicsOptions.cs`](../../src/Weather.Application/Common/Messaging/TopicsOptions.cs) and siblings in `services/Payments/`, `services/Notifications/`). All new services (Catalog, Basket, Ordering, Inventory, Payments, Invoicing) MUST set `DltTopicSuffix = ".DLT"` in `appsettings.json` under the Kafka topics section for consistency.

**Partition count for DLT topics:** same as the originating topic so that partition-key affinity is preserved for diagnostic grouping. Retention on DLT topics is **14 days** by default — long enough for on-call investigation, short enough to avoid unbounded growth.

---

## 2. Retry Policy (Default for All Consumers)

**There is currently no generic `Platform.KafkaFlow.Retry` middleware in this repo.** Consumer-side retry is achieved via two complementary mechanisms:

1. **Transient-error retry inside the handler** — the consumer handler (or the inner HTTP/DB call) uses Polly (established pattern — see [`src/Weather.Infrastructure/Common/HttpClientsDependencyInjection.cs`](../../src/Weather.Infrastructure/Common/HttpClientsDependencyInjection.cs) `AddRetry` with `HttpRetryStrategyOptions`) for upstream HTTP calls, and an EF Core execution strategy with retries for DB calls. These retries happen inline and do NOT re-consume the Kafka message.
2. **Broker-level redelivery on throw** — if the handler does not catch an exception, [`DeadLetterMiddleware`](../../platform/Platform.KafkaFlow.DeadLetter/DeadLetterMiddleware.cs) catches it and routes the message directly to the DLT topic. The offset is then committed. **There is no second attempt on the original topic.**

This is an intentionally **aggressive** DLT policy: a single unhandled exception dead-letters. It avoids partition head-of-line blocking on a poison message and surfaces bugs quickly in the reference solution. If real workload pressure motivates retry-with-backoff-then-DLT in the future, a `Platform.KafkaFlow.Retry` middleware can be introduced following the `DeadLetterMiddleware` pattern; the per-topic table in § 3 is already shaped to carry retry counts and backoff values.

**For the purposes of this document and the reference architecture, the *target* default policy is:**

| Stage | Behavior |
|-------|----------|
| 1st delivery | Handler runs; inner Polly/EF retries (≤ 3, jittered exponential) handle transient upstream faults |
| Handler throws | Exception caught by `DeadLetterMiddleware` → message produced to `{topic}.DLT` with diagnostic headers → offset committed → NO re-consume |
| Handler returns `Result.Fail(userError)` | Consumer handler **MUST** convert the failure into a business outcome event (e.g., `StockReservationFailedEvent`) and commit normally — DO NOT throw to DLT |

**Exceptions to the "throw → DLT" rule:**

- **`OperationCanceledException`** (graceful shutdown): rethrown by `DeadLetterMiddleware` so the offset remains uncommitted and the message is replayed on restart. This is correct — shutdown is not a poison-message condition.
- **Inbox-deduped duplicates**: handled by [`Platform.KafkaFlow.Inbox.EFCore`](../../platform/Platform.KafkaFlow.Inbox.EFCore/) middleware BEFORE the handler runs; duplicates become a no-op and commit — never dead-lettered.
- **Business-expected failures** (e.g., `InsufficientStockError` returned from `ReserveStockCommand` handler): the handler publishes `StockReservationFailedEvent`, returns `Result.Ok()`, and the message commits. Dead-lettering would wrongly classify a normal business outcome as a poison message. The architecture test in [architecture-tests.md § 1.5](architecture-tests.md) enforces this by rejecting handlers that rethrow `Result.Fail` as `InvalidOperationException` for user-actionable errors.

---

## 3. Per-Topic DLT Table

All topic names, partition counts and retention values below must match [events-catalog.md § 4 and § 6.1](events-catalog.md). DLT partition count equals the originating topic partition count.

| Topic | DLT topic | Source retention | DLT retention | Notes |
|-------|-----------|------------------|---------------|-------|
| `catalog.products` | `catalog.products.DLT` | infinite (audit) | 14 days | Event log — DLT messages indicate projection or consumer bug (BFF cache invalidator, Inventory stock-init, Basket ACL cache) |
| `catalog.categories` | `catalog.categories.DLT` | infinite (audit) | 14 days | Category taxonomy — low traffic; DLT rarely non-empty |
| `basket.sessions` | `basket.sessions.DLT` | 30 days | 14 days | Only the Checkout saga consumes; DLT alert = saga cannot start checkout |
| `ordering.orders` | `ordering.orders.DLT` | infinite (audit) | 14 days | Multiple consumers (Checkout saga, Notifications, BFF cache invalidator). A consumer-group-scoped DLT is NOT used — inspectors filter by `DLT-Original-Consumer-Group` header (future enhancement; header not yet in `DltHeaders`, see § 7) |
| `ordering.order-commands` | `ordering.order-commands.DLT` | 7 days | 14 days | Commands **must** complete — DLT = urgent ops investigation (saga step blocked) |
| `inventory.stock-events` | `inventory.stock-events.DLT` | infinite (audit) | 14 days | Catalog / BFF cache invalidator consume |
| `inventory.reservations` | `inventory.reservations.DLT` | infinite (audit) | 14 days | Saga fan-in depends on these — DLT delays saga completion until state-timeout triggers compensation |
| `inventory.reservation-commands` | `inventory.reservation-commands.DLT` | 7 days | 14 days | Commands must complete — DLT = saga state-timeout + compensation imminent |
| `payments.transactions` | `payments.transactions.DLT` | (existing) | 14 days | Existing — unchanged |
| `payments.payment-commands` | `payments.payment-commands.DLT` | (existing) | 14 days | Existing — unchanged |
| `notification.commands` | `notification.commands.DLT` | (existing) | 14 days | Existing — unchanged |

**Docker-compose note:** the DLT topics are NOT pre-created by the `kafka-create-topic` block; they are auto-created on first produce by the Kafka broker with cluster-default partitioning (3) and default retention (7 days). If exact parity with the source-topic partition count or a longer retention is required, add explicit `kafka-topics --create` lines to the bootstrap command for each `.DLT` topic. This is a pending operational task — logged as a follow-up in § 7.

---

## 4. Poison Message Handling Process

Standard procedure when a DLT message appears:

1. **Detection.** Message fails once → routed to `{topic}.DLT` by `DeadLetterMiddleware`. Headers preserved from the original message plus diagnostic headers (see [`DltHeaders`](../../platform/Platform.KafkaFlow.DeadLetter/DltHeaders.cs)):
   - `DLT-Original-Topic`, `DLT-Original-Partition`, `DLT-Original-Offset`
   - `DLT-Exception-Type`, `DLT-Exception-Message`, `DLT-Exception-StackTrace`
2. **Alert.** Grafana panel on `kafka.consumer.dlt.messages_total{topic="..."} > 0` (or equivalent delta over 5 min) → PagerDuty. One alert per consumer-group × topic combination.
3. **Inspection.** Operator opens AKHQ (`http://localhost:9000` in dev; cluster URL in staging/prod), navigates to the `.DLT` topic, reads the last N messages. Headers reveal: originating topic, consumer group (inferred from app logs since the header is not yet emitted — see § 7), exception class + full stack trace, and the original Avro key for correlation.
4. **Classification.**
   - **Code bug** → fix in codebase, deploy fix, replay per § 5.
   - **Data corruption** (unexpected payload shape, missing referenced aggregate) → write a correction script if needed, replay per § 5.
   - **Stale / obsolete** (e.g., replayed after business-level compensation already closed the issue) → mark as resolved in incident log; DO NOT replay. Document why in the incident postmortem.
5. **Postmortem.** For any DLT incident on `ordering.order-commands.DLT` or `inventory.reservation-commands.DLT` (saga-critical), a blameless postmortem is mandatory — these indicate the saga was blocked and the user experience degraded.

---

## 5. DLT Replay Runbook

Replay is performed by a **dedicated admin consumer group** reading from the DLT topic, optionally transforming the payload, and producing back to the original topic. The originating consumer group then re-consumes via its normal offset position.

**Step-by-step (local / dev):**

```bash
# 1. Discover the DLT message key + payload
docker compose exec kafka kafka-console-consumer \
  --bootstrap-server kafka:9092 \
  --topic ordering.order-commands.DLT \
  --group dlt-replay-admin-ordering \
  --from-beginning \
  --max-messages 5 \
  --property print.headers=true \
  --property print.key=true

# 2. Inspect stack trace header to confirm root cause fixed in latest deploy

# 3. Re-produce the (corrected) payload to the original topic
#    - Key MUST match the DLT-Original-Key preserved in the message
#    - For Avro payloads, use the schema-registry-aware producer CLI from Confluent
docker compose exec kafka kafka-console-producer \
  --bootstrap-server kafka:9092 \
  --topic ordering.order-commands \
  --property parse.key=true \
  --property key.separator=: \
  < replay-payload.bin

# 4. Verify the consumer reprocessed and emitted the expected response event
docker compose exec kafka kafka-console-consumer \
  --bootstrap-server kafka:9092 \
  --topic ordering.orders \
  --from-beginning --max-messages 10
```

**Production replay** follows the same sequence but uses a `replay-admin-*` operator CLI (pending — see § 7) that:
- Reads the DLT topic in batches.
- Optionally transforms the payload (e.g., fix a malformed decimal).
- Produces back to the original topic using the same Avro serializer.
- Commits the DLT offset as it goes so a partial replay can resume.

**Safety rails for replay:**
- NEVER replay without confirming the originating bug is fixed AND deployed — otherwise the message will immediately return to DLT and the incident loops.
- NEVER replay a command message whose business context is stale (e.g., a `ReserveStockCommand` for a saga that has already compensated). Replaying would produce a phantom reservation; prefer "mark as resolved" without replay.
- ALWAYS use a separate consumer group (`dlt-replay-admin-*`) so the replay read doesn't advance the production consumer's offset.

---

## 6. Observability

Per [master design § 11.3](../eshop-master-design.md), `KafkaFlow.OpenTelemetry` auto-instruments consumers. For DLQ governance specifically, the following metrics / logs must be emitted:

| Signal | Source | Alert threshold |
|--------|--------|-----------------|
| `kafka.consumer.lag` per `{topic, consumer-group}` | KafkaFlow OpenTelemetry | > 1000 messages sustained 5 min |
| `kafka.consumer.dlt.messages_total` per `{topic, consumer-group}` (new counter — emit from `DeadLetterMiddleware`) | `DeadLetterMiddleware` (implementation follow-up) | **any delta → PagerDuty** for command / saga-critical topics; dashboard only for read-model topics |
| `kafka.consumer.errors_total{exception_type="DataIntegrityException"}` | KafkaFlow OpenTelemetry tracing | any delta → investigation ticket |
| ILogger error log on DLT route (existing — `DeadLetterMiddleware` line 58) | `DeadLetterMiddleware` | structured log → Seq / Grafana Loki |
| OpenTelemetry span attributes on DLT route: `messaging.kafka.dlt.reason`, `messaging.kafka.dlt.original_topic` | instrumentation (follow-up) | trace-based alert if repeated for same key |

**Dashboard:** a shared `Kafka Consumer Health` Grafana dashboard is the canonical operator view. Panels: consumer lag (per consumer group), DLT message rate (per topic), exception-type breakdown (pie chart), last N DLT messages (table with headers). Dashboard JSON is a forward-looking deliverable — see § 7.

---

## 7. Follow-Up Work (for the implementation wave)

The items below are explicit gaps between this design document and the current platform code. Each must be tracked by the implementation agent touching the relevant layer.

| # | Gap | Owner |
|---|-----|-------|
| F-1 | `DltHeaders` missing `DLT-Consumer-Group` — makes multi-consumer topic DLT inspection harder | Platform / KafkaFlow DeadLetter maintainer |
| F-2 | No `kafka.consumer.dlt.messages_total` counter emitted — alert can't fire without metric | Platform / KafkaFlow DeadLetter maintainer |
| F-3 | No bootstrap block creating `{topic}.DLT` topics with explicit partition count + 14-day retention — currently auto-created with broker defaults | Implementation agent editing [docker-compose.yaml](../../docker-compose.yaml) |
| F-4 | No `replay-admin-*` operator CLI | Ops / post-v1 |
| F-5 | No `Platform.KafkaFlow.Retry` middleware (mentioned in some early design drafts) — intentionally OMITTED for v1; add only if production pressure requires retry-with-backoff-then-DLT | N/A unless requested |
| F-6 | Grafana `Kafka Consumer Health` dashboard JSON not checked in (`ops/grafana/kafka-consumer-health.json` referenced but not present) | Ops / post-v1 |

---

## 8. Cross-References

- [error-taxonomy.md](error-taxonomy.md) — which errors route to DLT vs. propagate as business outcomes
- [events-catalog.md § 4](events-catalog.md) — authoritative topic / retention / partition-count table
- [events-catalog.md § 7](events-catalog.md) — inbox registration (dedup BEFORE handler — DLT comes after)
- [use-cases.md § 3.3](use-cases.md) (Ordering saga consumers) and [§ 4.3](use-cases.md) (Inventory saga consumers) — consumer handler shape that converts `Result.Fail(userError)` to business outcome events instead of throwing
- [checkout-saga.md](checkout-saga.md) — saga state timeouts that complement DLT (when a command message is stuck in DLT, the corresponding response event never arrives; the saga state timeout triggers compensation)
- [`platform/Platform.KafkaFlow.DeadLetter/README.md`](../../platform/Platform.KafkaFlow.DeadLetter/README.md) — middleware registration snippet
