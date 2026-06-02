# ADR-0027: Kafka consumer partition-assignment strategy — CooperativeSticky for rolling/canary deploys

## Status

Accepted (2026-06-02)

## Context

Every Kafka consumer in the solution runs in Kubernetes under **rolling** and
**canary** deployments. Consumers span two messaging stacks:

- **6 bounded contexts on KafkaFlow 4.2.0** — 10 consumer registrations across
  Catalog, Inventory (×3), Invoicing (×3), Notifications, Ordering, Payments.
- **The saga on MassTransit.Kafka 8.5.7** — 2 consumer groups
  (`saga-checkout`, `saga-payment-processing`).

Both stacks wrap `Confluent.Kafka` (librdkafka), whose default for
`partition.assignment.strategy` is **`range,roundrobin`** — both **eager**
("stop-the-world") protocols.

**Eager rebalancing is hostile to rolling/canary deploys.** Under the eager
protocol, *any* group membership change revokes **all** partitions from **all**
members, then reassigns from scratch; no member in the group processes during the
rebalance. A rolling deploy bounces every pod, so an `N`-pod group incurs up to
`2N` full stop-the-world rebalances (one as each old pod leaves, one as each new
pod joins), and a canary adds a further join+leave. The cost is twofold:

1. **Processing stalls** repeated once per pod, group-wide — exactly when the
   system is already under deploy churn.
2. **Duplicate reprocessing** proportional to fleet size: every revoked-then-
   reassigned partition resumes from its last commit, so in-flight-but-uncommitted
   messages are re-fetched on each of the `2N` rebalances.

### What the research says

- **KIP-429 incremental cooperative rebalancing** is the industry answer.
  `CooperativeStickyAssignor` revokes **only** the partitions that must move to
  rebalance the group; every other partition keeps processing across the
  rebalance. Confluent recommends it for any consumer group that scales or
  redeploys. ([Confluent — Incremental Cooperative Rebalancing](https://www.confluent.io/blog/incremental-cooperative-rebalancing-in-kafka/))
- The older **`StickyAssignor`** also minimises partition *movement* but is still
  an **eager** protocol — it stops the world to compute the sticky assignment.
  Movement-minimisation without the cooperative protocol does not remove the
  stall. CooperativeSticky is the modern default.
- **The cooperative protocol changes nothing about delivery semantics.** Offsets
  are still committed per partition; processing is still at-least-once and
  per-partition ordering is preserved. It is purely a rebalance-protocol change.

## Decision Drivers (ranked)

1. **No stop-the-world stall on rolling/canary deploys** — partitions not being
   reassigned must keep processing through a rebalance.
2. **Minimise duplicate reprocessing during deploys** — only moved partitions
   should re-fetch from their last commit.
3. **One uniform protocol across every consumer group, both stacks** — the
   benefit is per-group; a single laggard group left on eager re-introduces the
   stall for its own deploys.
4. **No change to at-least-once or per-partition ordering** — this is a deploy
   optimisation, not a delivery-semantics change.
5. **Minimal config surface; the strategy is an architectural invariant** — it is
   a fixed solution-wide choice, never a per-environment tuning knob (unlike
   `auto.offset.reset`).
6. **Works for stateless Deployments** — no new identity infrastructure.

## Considered Options

### Option 1: Keep the eager default (`range,roundrobin`)

Do nothing.

**Pros:** zero work; the librdkafka default.
**Cons:** the stop-the-world behaviour above on every rolling/canary deploy —
the exact problem. Fails drivers 1 + 2.

### Option 2: Eager `StickyAssignor`

Switch to the sticky *eager* assignor (minimises partition movement).

**Pros:** fewer partitions change owner per rebalance, so less re-fetch than
`range`.
**Cons:** still an **eager** protocol — the whole group stops the world to
compute the assignment. Reduces driver 2 but not driver 1. Strictly dominated by
Option 3.

### Option 3: `CooperativeStickyAssignor` (chosen)

Switch every consumer group to the cooperative incremental protocol.

**Pros:** satisfies drivers 1 + 2 simultaneously — unmoved partitions keep
processing, and only moved partitions re-fetch. No change to ordering or
at-least-once (driver 4). Supported by both stacks (see Rationale). Works for
stateless Deployments (driver 6).
**Cons:** under a cooperative strategy KafkaFlow forces `EnableAutoCommit = true`
(see Consequences); a new consumer that forgets to opt in silently falls back to
eager.

### Placement sub-decision — where the setting lives

The strategy is bindable from appsettings (the BC consumer-options classes
inherit `Confluent.Kafka.ConsumerConfig`), so two homes were possible:

- **(a) appsettings, per consumer section.** Matches where `AutoOffsetReset`
  lives. **Rejected:** the value would be copy-pasted across 10 BC sections and a
  future consumer that omits it silently reverts to eager (driver 3); and the
  KafkaFlow `EnableAutoCommit` coupling stays invisible in config.
- **(b) code, one shared place per stack (chosen).** A single
  `WithCooperativeRebalancing()` extension for the BCs and a single shared saga
  `ConfigureCommon` helper. The **value** lives in exactly one place per stack,
  cannot drift, and is co-located with the documented `EnableAutoCommit`
  consequence. Treats the strategy as the architectural invariant it is
  (driver 5).

## Evaluation Matrix

| Driver (ranked) | 1: Eager default | 2: Eager Sticky | 3: CooperativeSticky |
|---|---|---|---|
| 1. No stop-the-world on deploy | ❌ full stall | ❌ still eager | ✅ unmoved partitions keep running |
| 2. Minimise duplicate reprocessing | ❌ all partitions re-fetch | ⚠️ less movement, still stops | ✅ only moved partitions re-fetch |
| 3. Uniform across groups | n/a | n/a | ✅ one protocol everywhere |
| 4. At-least-once + ordering unchanged | ✅ | ✅ | ✅ |
| 5. Minimal/invariant config | ⚠️ implicit default | ❌ per-knob | ✅ one place per stack |
| 6. Stateless Deployments | ✅ | ✅ | ✅ |

## Decision

Adopt **`CooperativeStickyAssignor` (`PartitionAssignmentStrategy.CooperativeSticky`)
for every consumer group in the solution** — all 10 KafkaFlow BC consumers and
both MassTransit saga groups — set **in code, one shared place per stack**
(placement option (b)).

- **BCs:** a `WithCooperativeRebalancing()` extension on `ConsumerConfig` in
  `Platform.KafkaFlow.DeadLetter`, chained into each consumer's
  `WithConsumerConfig(...)`.
- **Saga:** a shared `SagaKafkaConsumerDefaults.ConfigureCommon(...)` applied to
  every topic endpoint of both saga groups (this also retired the duplicated
  inline consumer config that previously existed only on the
  `saga-payment-processing` side).

**Cutover is a single clean deploy.** The repo is a non-production reference
solution with no live consumer group whose state must be preserved across the
switch ([ADR-0009](0009-reference-solution-target-profile.md)); environments come
up on fresh groups, so the eager→cooperative live-migration dance (a two-phase
`cooperative-sticky,range` rolling bounce) is deliberately **not** implemented or
documented here.

## Rationale

**Both stacks support the cooperative protocol natively.** KafkaFlow 4.2.0
classifies `Range`/`RoundRobin` as "stop-the-world" via an internal
`IsStopTheWorldStrategy` check and treats every other strategy — including
`CooperativeSticky` — incrementally: assigned partitions are `Union`-ed and
revoked partitions `Except`-ed from the live assignment, restarting only the
affected workers (it ships a cooperative-sticky sample and cooperative
consumer-manager tests). MassTransit.Kafka exposes
`PartitionAssignmentStrategy` on `IKafkaTopicReceiveEndpointConfigurator`, which
flows straight to the underlying `Confluent.Kafka` consumer builder.

**Eager Sticky is strictly dominated.** It addresses only the movement cost, not
the stop-the-world stall, so it cannot satisfy driver 1. CooperativeSticky
addresses both for the same one-line change.

**The strategy is an invariant, so it belongs in code.** Unlike
`auto.offset.reset` (which legitimately varies per environment), "all consumer
groups rebalance cooperatively" is a fixed architectural property. Putting the
value in one shared helper per stack makes it un-drift-able and keeps the
KafkaFlow `EnableAutoCommit` coupling discoverable next to the setting.

## Consequences

### Positive

- Rolling and canary deploys no longer stop the world: partitions not being
  reassigned keep processing, and only moved partitions re-fetch from their last
  commit. The stall and the duplicate-reprocessing blast radius both shrink from
  "whole group, `2N` times" to "the few partitions that actually moved."
- Per-partition ordering and at-least-once delivery are unchanged.
- One uniform protocol across both stacks; the value cannot drift between the two
  saga groups (now sharing `ConfigureCommon`) or between BCs (one extension).

### Negative

- **KafkaFlow forces `EnableAutoCommit = true` under a cooperative strategy.**
  KafkaFlow still sets `EnableAutoOffsetStore = false` and calls `StoreOffset()`
  only after a message is processed, so delivery stays at-least-once; the flush
  of stored offsets is just delegated to librdkafka's background auto-commit
  (`auto.commit.interval.ms`, default 5 s) instead of KafkaFlow's eager
  committer, because incremental revocation must commit per-partition at the
  rebalance callback. The practical effect is a slightly **wider duplicate-
  redelivery window** (≤ ~5 s of processed-but-not-yet-committed messages on a
  crash/rebalance), which the **inbox dedup** middleware absorbs — every consumer
  already short-circuits redeliveries before the handler runs
  ([conventions.md §6](../bc-design/conventions.md), [ADR-0025](0025-kafka-consumer-retry-dlt-policy.md)).
- Because that override is unconditional, the per-consumer `EnableAutoCommit:
  false` lines previously in the BC appsettings were silent no-ops once the
  strategy changed; they were **removed** so config no longer misrepresents
  runtime. The single explanation lives in this ADR + the
  `WithCooperativeRebalancing` XML docs.

### Risks

- **Framework-specific cooperative handling.** KafkaFlow's incremental path is
  verified against its source + sample. MassTransit *exposes* the setting and
  wires it to the Confluent consumer, but the saga's cooperative behaviour should
  be **confirmed at runtime** before being trusted under real deploy churn.
- **A new consumer that omits the helper silently reverts to eager.** Neither a
  shared helper nor appsettings fully prevents this; only an architecture test
  would, and a structural-invariant arch test of that shape is deliberately not
  adopted here. Mitigation: a single obvious helper per stack + this ADR; the
  symptom (stop-the-world on that one group's deploys) is observable.

## Implementation Notes

- **Shared helper (BCs):** `KafkaConsumerRebalancing.WithCooperativeRebalancing(this
  ConsumerConfig)` in `Platform.KafkaFlow.DeadLetter` (sets
  `PartitionAssignmentStrategy = CooperativeSticky`). The csproj gains an explicit
  `Confluent.Kafka` reference (version pinned in `platform/Directory.Packages.props`).
- **BC wiring:** chained into `WithConsumerConfig(consumerOptions.WithCooperativeRebalancing())`
  at all 10 consumer sites (Catalog, Inventory ×3, Invoicing ×3, Notifications,
  Ordering, Payments).
- **Saga:** new `SagaKafkaConsumerDefaults.ConfigureCommon(...)` (offset-reset +
  `CooperativeSticky` + Avro deserializers); `CheckoutSagaDependencyInjection` and
  `PaymentProcessingSagaDependencyInjection` both call it, removing the previously
  duplicated inline config.
- **appsettings:** the 9 dead `"EnableAutoCommit": false` lines removed from the
  five BC appsettings that carried them (Ordering, Payments, Notifications,
  Inventory ×3, Invoicing ×3). `AutoCommitIntervalMs` is left at the librdkafka
  default.
- **Producers are out of scope** — the outbox relay and any producer pick
  partitions via the producer `partitioner`, an unrelated knob; this ADR changes
  only the consumer group rebalance protocol.

### When to revisit

- If services later adopt stable per-pod identity (StatefulSets) and the residual
  canary/restart rebalance cost proves material, **static group membership**
  (`group.instance.id`, KIP-345) is the complementary next step — a separate ADR,
  additive to this one.

## Related Decisions

- [ADR-0001: Centralized Saga Orchestration](0001-centralized-saga-orchestration.md) — the saga consumer groups whose deploys this protects.
- [ADR-0009: Reference-Solution Target Profile](0009-reference-solution-target-profile.md) — the non-production posture behind the single-cutover decision.
- [ADR-0025: Kafka consumer retry & dead-letter policy](0025-kafka-consumer-retry-dlt-policy.md) — the consumer middleware chain this sits alongside; shares the `Platform.KafkaFlow.DeadLetter` home.
- [conventions.md §6](../bc-design/conventions.md) — at-least-once + inbox dedup, which absorbs the wider duplicate window.
