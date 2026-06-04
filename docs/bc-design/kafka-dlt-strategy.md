# Kafka DLT & Retry Strategy

> Governance for dead-lettering and consumer retries across every eShop topic. Reuses the existing [`Platform.KafkaFlow.DeadLetter`](../../platform/Platform.KafkaFlow.DeadLetter/) middleware — this document specifies **per-topic configuration** and **operational procedures** so consumers are dead-letter-aware from day one.
>
> Terminology: this repo uses **DLT** (Dead-Letter **Topic**) everywhere — file names, topic identifiers, metrics, and prose. That's what Kafka actually provides (a topic), and what `TopicsOptions.DltTopicSuffix` and `Platform.KafkaFlow.DeadLetter` already emit. Older write-ups may use the generic dead-letter-queue acronym as a synonym; treat any such occurrence in this repo as a doc-rot artefact and substitute the canonical term.

---

## 1. DLT Naming Convention

`{source-topic}.{consumer-bc}.DLT` — the actual broker topic name as produced by the live code. Examples:

- `payments.payment-commands.Payments.DLT` (Payments BC's consumer of `payments.payment-commands`)
- `inventory.reservation-commands.Inventory.DLT` (Inventory BC's consumer of `inventory.reservation-commands`)
- `ordering.orders.Invoicing.DLT` AND `ordering.orders.Inventory.DLT` (the SAME source topic, two different consumer BCs, two different DLT buckets)

The suffix is applied by [`DeadLetterMiddleware`](../../platform/Platform.KafkaFlow.DeadLetter/DeadLetterMiddleware.cs) using the configured `DltTopicSuffix` from each service's `TopicsOptions` (see e.g. [`services/Payments/Payments.Application/Common/Messaging/TopicsOptions.cs`](../../services/Payments/Payments.Application/Common/Messaging/TopicsOptions.cs) and sibling files in every BC's Application or Infrastructure project). Each BC's `appsettings.json` Topics section pins its own suffix:

| BC | `DltTopicSuffix` |
|----|------------------|
| Basket | `.Basket.DLT` |
| Catalog | `.Catalog.DLT` |
| Inventory | `.Inventory.DLT` |
| Invoicing | `.Invoicing.DLT` |
| Notifications | `.Notifications.DLT` |
| Ordering | `.Ordering.DLT` |
| Payments | `.Payments.DLT` |

**Per-consumer-BC isolation is intentional.** When two BCs consume the same source topic, each gets its own DLT bucket so the on-call mapping (DLT → ownership) is unambiguous — no need for a `DLT-Consumer-Group` header to disambiguate. F-1 in § 7 becomes a doc-only nice-to-have once this naming is honoured.

**Saga (MassTransit) does NOT use `Platform.KafkaFlow.DeadLetter`.** Saga consumers in `saga/SagaOrchestrators/` rely on MassTransit's built-in retry + the saga state-timeout machinery documented in [saga-stuck-runbook.md](saga-stuck-runbook.md). Saga-consumed event topics (`basket.sessions`, `ordering.orders`, `inventory.reservations`, `payments.transactions`) therefore do not have a `*.Saga.DLT` counterpart at all.

**Partition count for DLT topics:** same as the originating topic so that partition-key affinity is preserved for diagnostic grouping. Retention on DLT topics is **14 days** by default — long enough for on-call investigation, short enough to avoid unbounded growth.

---

## 2. Retry Policy (Default for All Consumers)

Every consumer wires one **classified `RetryForever`** middleware ahead of the inbox ([ADR-0025](../adr/0025-kafka-consumer-retry-dlt-policy.md)). The retry decision branches on failure **class**, not exception type, via the shared predicate [`ConsumerRetry.IsRetryable`](../../platform/Platform.KafkaFlow.DeadLetter/ConsumerRetry.cs):

1. **Retryable** (transient infrastructure fault, or a deliberate `RetryableException` / `System.TimeoutException`) → retried **forever with the consumer paused**, never dead-lettered. "Transient" is whatever Npgsql flags `DbException.IsTransient` (SQLSTATE classes `08*`, `40001`, `40P01`, `53*`, `57P0*`, `58*`). A DB outage therefore parks the partition until recovery — no message lost, no DLT flood.
2. **Poison** (everything else: integrity / data / syntax violations `23*`/`22*`/`42*`, deserialization failures, a bare `DbUpdateException`, domain bugs) → **not** handled by the retry middleware, so it falls through to [`DeadLetterMiddleware`](../../platform/Platform.KafkaFlow.DeadLetter/DeadLetterMiddleware.cs) and is produced to the DLT immediately; the partition advances.

A message on a DLT now means **"genuinely unprocessable (poison)"** — never "the DB blipped."

**The default policy, per delivery:**

| Stage | Behavior |
|-------|----------|
| Handler throws a **retryable** failure | Classified `RetryForever` retries with backoff (500 ms → 1 s → 2 s → 5 s), consumer **paused**; offset NOT committed; repeats until success — never dead-lettered |
| Handler throws a **poison** failure | Not handled by retry → `DeadLetterMiddleware` produces the message to `<source-topic>.<consumer-bc>.DLT` with diagnostic headers → offset committed → partition advances |
| Handler returns `Result.Fail(userError)` | Consumer handler **MUST** convert the failure into a business outcome event (e.g., `StockReservationFailedEvent`) and commit normally — DO NOT throw to DLT |

**Exceptions to the "throw → DLT" rule:**

- **`OperationCanceledException`** (graceful shutdown): excluded from the retryable set by `ConsumerRetry.IsRetryable` and rethrown by `DeadLetterMiddleware`, so the offset remains uncommitted and the message is replayed on restart. This is correct — shutdown is neither a poison nor a transient condition.
- **Inbox-deduped duplicates**: handled by [`Platform.KafkaFlow.Inbox.EFCore`](../../platform/Platform.KafkaFlow.Inbox.EFCore/) middleware BEFORE the handler runs; duplicates become a no-op and commit — never dead-lettered.
- **Business-expected failures** (e.g., `InsufficientStockError` returned from `ReserveStockCommand` handler): the handler publishes `StockReservationFailedEvent`, returns `Result.Ok()`, and the message commits. Dead-lettering would wrongly classify a normal business outcome as a poison message. The architecture test in [architecture-tests.md § 1.5](architecture-tests.md) enforces this by rejecting handlers that rethrow `Result.Fail` as `InvalidOperationException` for user-actionable errors.

---

## 3. Per-Consumer DLT Table

DLT topic names are **derived**, not an independent fact: `{source-topic}.{consumer-bc}.DLT` (§ 1), where `{consumer-bc}` is the BC's `DltTopicSuffix` (§ 1 table) and the consumer→source-topic set is the [events-catalog.md § 2](events-catalog.md) consumer registry. Source-topic partitions/retention live in [kafka-topology.md](../kafka-topology.md) and are **not** repeated here (per [ADR-0033](../adr/0033-kafka-topic-contract-doc-ssot.md)). DLT retention is **14 days** across the board (`retention.ms=1209600000`); the 10 live DLT topics below are pre-created with 3 partitions (matching source) in the `kafka-create-topic` block of [docker-compose.yaml](../../docker-compose.yaml).

The table is the concrete on-call enumeration; it carries no fact not already derivable from the rule above + the two anchors.

| Consumer BC | Source topic | DLT topic (broker name) | Notes |
|-------------|--------------|--------------------------|-------|
| Catalog | `inventory.stock-events` | `inventory.stock-events.Catalog.DLT` | `StockLevelChangedKafkaHandler` projects to `Catalog.IsSellable`. |
| Inventory | `inventory.reservation-commands` | `inventory.reservation-commands.Inventory.DLT` | Saga → Inventory commands. DLT = saga state-timeout + compensation imminent. |
| Inventory | `catalog.products` | `catalog.products.Inventory.DLT` | Stock-init projection from Catalog product master. |
| Inventory | `ordering.orders` | `ordering.orders.Inventory.DLT` | `OrderCancelledEvent` consumer (release reserved stock). |
| Invoicing | `ordering.orders` | `ordering.orders.Invoicing.DLT` | `OrderConfirmedEvent` + `OrderCancelledEvent` → invoice issuance / credit-note. |
| Invoicing | `payments.transactions` | `payments.transactions.Invoicing.DLT` | `PaymentCapturedEvent` + `PaymentRefundedEvent` → invoice issuance trigger. |
| Invoicing | `notifications.notify-events` | `notifications.notify-events.Invoicing.DLT` | `NotificationDeliveryStatusChangedEvent` delivery-confirmation projection. |
| Notifications | `notifications.notify-commands` | `notifications.notify-commands.Notifications.DLT` | `NotifyUserCommand` consumer (the sole inbound for Notifications). |
| Ordering | `ordering.order-commands` | `ordering.order-commands.Ordering.DLT` | Saga → Ordering commands. DLT = urgent ops investigation (saga step blocked). |
| Payments | `payments.payment-commands` | `payments.payment-commands.Payments.DLT` | Saga → Payments commands (`AuthorizePayment`, `CapturePayment`, `VoidPayment`, `RequestRefund`). |

**Source topics with no DLT (saga-consumed):**

The Checkout saga in `saga/SagaOrchestrators/` consumes `basket.sessions`, `ordering.orders`, `inventory.reservations`, and `payments.transactions` via MassTransit. Saga does NOT wire `Platform.KafkaFlow.DeadLetter` (no `.AddDeadLetter()` calls under `saga/`); on consumer failure it relies on MassTransit's redelivery + the saga state-timeout machinery (see [saga-stuck-runbook.md § 4](saga-stuck-runbook.md)). There is therefore no `*.Saga.DLT` topic on the broker.

**Source topics with no current consumer:**

`catalog.categories`, `inventory.stock-events`, `inventory.reservations` (saga-only; see above) — no BC currently registers a `DeadLetterMiddleware`-wrapped consumer. If a consumer lands later it will produce to `<topic>.<BC>.DLT` per the convention in § 1.

**Docker-compose note:** the 10 per-consumer-BC DLT topics from § 3 are pre-created by the `kafka-create-topic` block with 3 partitions (matching source) + **14-day** retention (`retention.ms=1209600000`) + `min.insync.replicas=1` — see resolved F-3 in § 7. Any DLT not in that list (e.g. a future consumer's) is auto-created on first produce at the cluster default (3 partitions, broker-default 7-day retention) until it is added to the pre-create block.

---

## 4. Poison Message Handling Process

Standard procedure when a DLT message appears:

1. **Detection.** Message fails once → routed to `<source-topic>.<consumer-bc>.DLT` (e.g. `payments.payment-commands.Payments.DLT`) by `DeadLetterMiddleware`. Headers preserved from the original message plus diagnostic headers (see [`DltHeaders`](../../platform/Platform.KafkaFlow.DeadLetter/DltHeaders.cs)):
   - `DLT-Original-Topic`, `DLT-Original-Partition`, `DLT-Original-Offset`
   - `DLT-Exception-Type`, `DLT-Exception-Message`, `DLT-Exception-StackTrace`
2. **Alert.** Grafana panel on `kafka.consumer.dlt.messages_total{topic="..."} > 0` (or equivalent delta over 5 min) → PagerDuty. One alert per consumer-group × topic combination.
3. **Inspection.** Operator opens AKHQ (`http://localhost:9000` in dev; cluster URL in staging/prod), navigates to the `.DLT` topic, reads the last N messages. Headers reveal: originating topic, consumer group (inferred from app logs since the header is not yet emitted — see § 7), exception class + full stack trace, and the original Avro key for correlation.
4. **Classification.**
   - **Code bug** → fix in codebase, deploy fix, replay per § 5.
   - **Data corruption** (unexpected payload shape, missing referenced aggregate) → write a correction script if needed, replay per § 5.
   - **Stale / obsolete** (e.g., replayed after business-level compensation already closed the issue) → mark as resolved in incident log; DO NOT replay. Document why in the incident postmortem.
5. **Postmortem.** For any DLT incident on `ordering.order-commands.Ordering.DLT`, `inventory.reservation-commands.Inventory.DLT`, or `payments.payment-commands.Payments.DLT` (the three saga-critical command DLTs), a blameless postmortem is mandatory — these indicate the saga was blocked and the user experience degraded.

---

## 5. DLT Replay Runbook

Replay is performed by a **dedicated admin consumer group** reading from the DLT topic, optionally transforming the payload, and producing back to the original topic. The originating consumer group then re-consumes via its normal offset position.

**Step-by-step (local / dev):**

```bash
# 1. Discover the DLT message key + payload
#    Topic name is <source-topic>.<consumer-bc>.DLT per § 1 (here: Ordering BC's
#    consumer of ordering.order-commands). For Inventory's reservation-commands
#    DLT use inventory.reservation-commands.Inventory.DLT, etc.
docker compose exec kafka kafka-console-consumer \
  --bootstrap-server kafka:9092 \
  --topic ordering.order-commands.Ordering.DLT \
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

Per [master design § 11.3](../eshop-master-design.md), `KafkaFlow.OpenTelemetry` auto-instruments consumers. For DLT governance specifically, the following metrics / logs must be emitted:

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
| F-1 | `DltHeaders` missing `DLT-Consumer-Group` — strictly speaking, the per-consumer-BC suffix in the DLT topic name (§ 1) already disambiguates ownership for the cases that matter today. The header is still nice-to-have for finer-grained scoping if a single BC ever runs multiple consumer groups against the same source topic. | Platform / KafkaFlow DeadLetter maintainer |
| F-2 | No `kafka.consumer.dlt.messages_total` counter emitted — alert can't fire without metric | Platform / KafkaFlow DeadLetter maintainer |
| ~~F-3~~ | ~~No bootstrap block creating `<source-topic>.<consumer-bc>.DLT` topics with explicit partition count + 14-day retention.~~ **Resolved** — the 10 per-consumer-BC DLT topics from § 3 are now pre-created by `kafka-create-topic` with 3 partitions (matching source) + 14d retention (`retention.ms=1209600000`) + `min.insync.replicas=1`. See [docker-compose.yaml](../../docker-compose.yaml) `kafka-create-topic` block. | — |
| F-4 | No `replay-admin-*` operator CLI | Ops — planned scope, see [roadmap.md § 2.4](../roadmap.md) |
| ~~F-5~~ | ~~No `Platform.KafkaFlow.Retry` middleware — intentionally OMITTED for v1.~~ **Resolved** — [ADR-0025](../adr/0025-kafka-consumer-retry-dlt-policy.md) adopted a classified `RetryForever` (transient → retry-forever-paused, poison → DLT) across all consumers via the shared [`ConsumerRetry.IsRetryable`](../../platform/Platform.KafkaFlow.DeadLetter/ConsumerRetry.cs) predicate. See § 2. | — |
| F-6 | Grafana `Kafka Consumer Health` dashboard JSON not checked in (`ops/grafana/kafka-consumer-health.json` referenced but not present) | Ops — planned scope, see [roadmap.md § 2.4](../roadmap.md) |

---

## 8. Cross-References

- [error-taxonomy.md](error-taxonomy.md) — which errors route to DLT vs. propagate as business outcomes
- [kafka-topology.md](../kafka-topology.md) — authoritative per-topic partitions / retention / class (the source-topic topology this DLT table no longer repeats)
- [events-catalog.md § 2](events-catalog.md) — per-event consumer registry (which BC consumes which source topic → which DLT)
- [events-catalog.md § 7](events-catalog.md) — inbox registration (dedup BEFORE handler — DLT comes after)
- [use-cases.md § 3.3](use-cases.md) (Ordering saga consumers) and [§ 4.3](use-cases.md) (Inventory saga consumers) — consumer handler shape that converts `Result.Fail(userError)` to business outcome events instead of throwing
- [checkout-saga.md](checkout-saga.md) — saga state timeouts that complement DLT (when a command message is stuck in DLT, the corresponding response event never arrives; the saga state timeout triggers compensation)
- [`platform/Platform.KafkaFlow.DeadLetter/README.md`](../../platform/Platform.KafkaFlow.DeadLetter/README.md) — middleware registration snippet
