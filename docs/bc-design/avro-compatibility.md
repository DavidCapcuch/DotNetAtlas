# Avro Schema Compatibility Modes

> Specifies per-topic Avro compatibility modes for the Confluent Schema Registry used by the eShop reference solution. This document is the authoritative spec for schema evolution; a corresponding **ADR-0007 (Avro Compatibility Policy)** will be authored alongside the implementation wave — when that ADR exists, it supersedes any conflicting clause here. Until then, this chapter IS the accepted policy.
>
> Scope: the 8 new topics introduced by the four new BCs (Catalog, Basket, Ordering, Inventory) plus the existing Payments / Notifications topics. Existing subjects in `platform/Platform.SchemaRegistry.Contracts/Avro/` retain their current compatibility until an explicit schema change is proposed.

---

## 1. Compatibility Modes in Confluent Schema Registry

Confluent Schema Registry supports seven compatibility settings per subject ([Confluent docs — Schema Evolution and Compatibility](https://docs.confluent.io/platform/current/schema-registry/avro.html)):

| Mode | Upgrade order guarantee | What producer may do | What consumer may do |
|------|------------------------|---------------------|---------------------|
| `NONE` | none | anything | anything (nothing guaranteed) |
| `BACKWARD` | **consumers first** | add optional fields, delete fields with defaults | read new messages with old schema |
| `BACKWARD_TRANSITIVE` | consumers first, across ALL versions | as BACKWARD but each new schema must be compatible with every prior version, not just the immediate predecessor | same |
| `FORWARD` | **producers first** | add fields, delete optional fields | old data readable by NEW schema (new fields get defaults) |
| `FORWARD_TRANSITIVE` | producers first, across ALL versions | as FORWARD transitive | same |
| `FULL` | either order | add optional fields, delete optional fields | both old↔new and new↔old data readable |
| `FULL_TRANSITIVE` | either order, across ALL versions | as FULL transitive | same |

Mnemonic: **BACKWARD = new consumer reads old data**; **FORWARD = old consumer reads new data**; **FULL = both** (the safest, most constrained).

For the eShop solution, we make compatibility decisions per **topic category** (event-log vs. command) because the deployment-order assumption differs fundamentally between the two.

---

## 2. Per-Topic Compatibility Decisions

### 2.1 Event-log topics → `FORWARD_TRANSITIVE`

**Topics in this category:**

- `catalog.products`
- `catalog.categories`
- `basket.sessions`
- `ordering.orders`
- `inventory.stock-events`
- `inventory.reservations`
- `payments.transactions` (existing — confirm setting during migration)

**Rationale:** Producers (the owning BC) deploy independently and lead consumers. A new producer version of Catalog may publish a richer `ProductCreatedEvent` while downstream consumers (Inventory's stock-init subscriber, BFF cache invalidator, Basket ACL cache, etc.) still run the previous code. Under `FORWARD_TRANSITIVE`, old consumers can still READ messages written by every newer schema version — new fields fall into the default; removed optional fields are re-materialized as their default. This is the deployment-order guarantee we want because the producer owns the event and evolves faster than any individual consumer.

**Why TRANSITIVE:** event-log topics have **infinite retention** (per [events-catalog.md § 4](events-catalog.md)). A consumer may read messages written under schema v1 next week even if v7 is current. Non-transitive FORWARD only guarantees compatibility with the immediate predecessor — that is insufficient for an audit-log topic. TRANSITIVE requires every new schema to be compatible with every prior version on the subject, matching the reality of infinite-retention consumption.

**Allowed changes under FORWARD_TRANSITIVE:**

| Change | Allowed? | Rule |
|--------|----------|------|
| Add a new field WITH a `default` | **Yes** | Old consumers see the default |
| Add a new field WITHOUT a default | No | Old consumers cannot parse |
| Remove an optional field (had a default) | **Yes** | Old consumers re-materialize as default |
| Remove a required field | No | Breaks old consumer decode |
| Change a field type | No | Must deprecate + introduce new field |
| Rename a field | No (schema alias required — and aliases don't always help TRANSITIVE) | Use deprecation + new field instead |
| Reorder fields | Yes (Avro is name-based — ordering irrelevant) | |
| Add / modify `doc` strings | Yes (docs don't break decode) | |
| Add an enum symbol | No under strict Avro; requires `default` enum value to be safe — prefer adding as a new field with a string | |

### 2.2 Command topics → `FULL_TRANSITIVE`

**Topics in this category:**

- `ordering.order-commands`
- `inventory.reservation-commands`
- `payments.payment-commands` (existing — confirm setting during migration)
- `notification.commands` (existing — confirm setting during migration)

**Rationale:** The saga (producer of commands) and the service (consumer of commands) evolve **independently in both directions**. A deployment may ship a new Ordering consumer before the saga redeploys with a new command schema; another deployment may ship a new saga producer before Ordering redeploys. `FULL` requires both directions compatible, so any valid deployment order works. **TRANSITIVE** is again required because command topics retain for 7 days — old saga state (MassTransit persisted state) can re-emit a retry command built against the schema version in effect when the saga instance started, and Ordering (already several versions ahead) must still decode it.

**Allowed changes under FULL_TRANSITIVE:**

| Change | Allowed? | Rule |
|--------|----------|------|
| Add a new field WITH a `default` | **Yes** | Both directions see the default |
| Remove an optional field (had a default) | **Yes — only if deprecated in prior version and no active instance references it** | |
| Remove a required field | No | Breaks both directions |
| Any field-type change | No | Introduce new field + deprecate old + remove in a later version |
| Rename a field | No | Same as above |

Command schema changes are the most constrained and should be rare; prefer introducing a V2 command record under a new subject rather than evolving a live command schema in place.

### 2.3 Compatibility Summary Table

| Topic | Mode | Breaking-change process |
|-------|------|------------------------|
| `catalog.products` | `FORWARD_TRANSITIVE` | add optional field → v1.1; deprecate field → v1.2; remove optional field → v1.3; breaking change → v2.0 under new subject (`Catalog.Products.ProductCreatedEventV2`) |
| `catalog.categories` | `FORWARD_TRANSITIVE` | as above |
| `basket.sessions` | `FORWARD_TRANSITIVE` | as above; note low cardinality of producers (only Basket) eases transition |
| `ordering.orders` | `FORWARD_TRANSITIVE` | as above; multiple consumers (saga, Notifications, BFF) — coordinate deprecation timelines |
| `ordering.order-commands` | `FULL_TRANSITIVE` | add optional field only; for any breaking change, register V2 record under new subject + emit both V1 and V2 during migration window + remove V1 after consumer migration |
| `inventory.stock-events` | `FORWARD_TRANSITIVE` | as event-log case |
| `inventory.reservations` | `FORWARD_TRANSITIVE` | as event-log case; saga consumes — saga state stores snapshot of fields it relies on, so adding fields is safe |
| `inventory.reservation-commands` | `FULL_TRANSITIVE` | command-topic process |
| `payments.transactions` (existing) | `FORWARD_TRANSITIVE` (to confirm) | migrate during next change; document current setting first |
| `payments.payment-commands` (existing) | `FULL_TRANSITIVE` (to confirm) | as above |
| `notification.commands` (existing) | `FULL_TRANSITIVE` (to confirm) | as above |

---

## 3. Subject Naming Strategy

The existing solution uses the **Record Name Strategy** for Schema Registry subjects (confirmed in [`platform/Platform.Test.Framework/Kafka/KafkaTestProducer.cs`](../../platform/Platform.Test.Framework/Kafka/KafkaTestProducer.cs) line 32 and [`KafkaTestContainer.cs`](../../platform/Platform.Test.Framework/Kafka/KafkaTestContainer.cs) line 113: `SubjectNameStrategy = SubjectNameStrategy.Record`). Under this strategy:

- Registry subject = `{Namespace}.{RecordName}` (e.g., `Catalog.Products.ProductCreatedEvent`).
- **One subject per Avro record**, regardless of how many topics carry messages of that record type. In practice each record is produced to a single topic, so subject ↔ record ↔ topic is 1:1:1 for most new events.
- Compatibility mode applies **per subject**, not per topic.

**Practical implication:** if two topics happened to share a record type (rare, not currently the case in the new design), changing the schema for one topic inherently changes it for the other. The compatibility constraints of the **strictest** consumer combine. This is another reason to keep per-topic compatibility uniform within a category (event-log vs. command).

**Avro namespacing (reiterating [master design § 3.2](../eshop-master-design.md)):** `{Domain}.{Aggregate}` — e.g., `Catalog.Products`, `Inventory.Reservations`, `Ordering.Orders`. Record names follow C# class names: `ProductCreatedEvent`, `ReserveStockCommand`, `OrderConfirmedEvent`. Full `.avsc` specifications live at `platform/Platform.SchemaRegistry.Contracts/Avro/{Domain}/{Aggregate}/{RecordName}.avsc` and are mastered in [events-catalog.md § 5](events-catalog.md).

---

## 4. Breaking-Change Process

Step-by-step for an event-log topic (FORWARD_TRANSITIVE). Command topics follow the same shape but with stricter allowed-change rules per § 2.2.

1. **Propose** the change in a design PR. Include:
   - Current schema + proposed schema diff.
   - Compatibility analysis vs. Schema Registry CLI (`schema-registry-compatibility-check` against the registered subject).
   - Downstream consumer impact (list consumers from [events-catalog.md § 6](events-catalog.md)).
2. **Write** the new `.avsc` with the change:
   - If **adding** a field, include `"default": <value>` or `"default": null` for nullable unions — mandatory under FORWARD and FULL.
   - If **removing** a field, ensure it had a default in the current registered schema (so old data can be read by the new schema). If it didn't, this step must be preceded by a deprecation release that adds the default first.
3. **Deploy producer.** Producer writes new schema to Schema Registry on first publish; registry validates compatibility; if incompatible, the deploy fails at the produce call. Fix and retry.
4. **Consumers** keep running the old code — they decode the new messages using the OLD reader schema; new fields are ignored by the decoder, removed-with-default fields resolve to the default.
5. **When convenient**, roll consumers to a newer code version that reads the new fields. No compatibility change needed (still FORWARD with new additions).
6. **To remove** a field that was required in the original schema:
   - Release N: add a replacement field (with default). Producers populate both old and new. No breaking change.
   - Release N+1: deprecate the old field in `doc` strings and in C# wrapper types. Producers keep writing both.
   - Release N+2: all consumers have been updated to read the new field. Producers stop writing the old field (now resolves to default in decode).
   - Release N+3: if truly needed, register a **V2 record** under a new subject (`*EventV2`). V1 subject is frozen. Topic can briefly carry both; after consumer migration, V1 producer retires.

**Command-topic note:** steps 3–5 are identical, but step 4 also requires consumers to continue to successfully PRODUCE RESPONSES that old callers can decode (that's the FULL half). In practice this means response-event schemas must be evolved in lock-step with command schemas, or response events must be kept stable while commands evolve first.

---

## 5. Schema Registry Enforcement

- **Producer-side enforcement.** [`UniversalAvroSerializer`](../../platform/Platform.Avro.UniversalSerDes/UniversalAvroSerializer.cs) registers the producer schema on first publish. If the registered compatibility mode rejects the new schema, the produce call throws `SchemaRegistryException` → the outbox relay retries; the poll loop will surface the error in logs. **The deploy effectively fails at first publish time** — operator must roll back or fix the schema.
- **CI-side enforcement (future — follow-up F-A below).** A CI step runs `schema-registry-compatibility-check` (Confluent CLI) against every `.avsc` file changed in a PR, using the canonical registry URL for staging. Incompatible schemas fail the PR check before merge. This catches breaking changes **before** production deploy rather than at runtime.
- **Bootstrap (one-time).** The compatibility mode for each subject must be set explicitly on initial subject creation — the registry default (`BACKWARD`) is not what we want for most subjects. This is done either via:
  - The registry admin REST API (`PUT /config/{subject}` with `{"compatibility": "FORWARD_TRANSITIVE"}`), or
  - An idempotent bootstrap job run once per environment during initial deploy.

  Implementation note: **the bootstrap job is a follow-up (see § 7 F-A).** Until it lands, operators must set compatibility manually via AKHQ (http://localhost:9000 in dev) or the registry's REST endpoint.

---

## 6. Evolution Anti-Patterns

Guard rails called out so implementation agents don't trip on them:

- **Do NOT** change `namespace` on a registered record — it changes the subject (under Record Name Strategy) and orphans the prior subject. If the record must move namespaces, treat it as a V2 migration (§ 4 step 6).
- **Do NOT** rely on Avro field aliases for renames under `FORWARD_TRANSITIVE`. Aliases work for BACKWARD scenarios but produce subtle bugs in forward-directional decoding; the safer pattern is add-new-field + deprecate-old.
- **Do NOT** widen a field's Avro union (e.g., `["null","string"]` → `["null","string","int"]`) — under FULL this is rejected; under FORWARD it breaks old consumers that don't expect the new branch.
- **Do NOT** change `default` values — changing a default is incompatible because old data produced without the field will now decode to a different value.
- **Do NOT** mix compatibility modes within a topic category — every event-log topic must be FORWARD_TRANSITIVE, every command topic FULL_TRANSITIVE. Mixed settings make cross-topic reasoning impossible.

---

## 7. Follow-Up Work (for the implementation wave and ADR-0007)

| # | Gap | Owner |
|---|-----|-------|
| F-A | Schema-Registry compatibility bootstrap: idempotent job setting per-subject compatibility modes matching § 2.3 | Implementation agent for the Platform.SchemaRegistry.Contracts layer |
| F-B | CI step running `schema-registry-compatibility-check` on every `.avsc` diff in a PR | CI / DevOps |
| F-C | ADR-0007 authoring — formalize these decisions as a standing ADR, cross-linked from [`docs/adr/README.md`](../adr/README.md) | Architecture team |
| F-D | Confirm / adjust compatibility for existing subjects (`Payments.Transactions.*`, `Notifications.*`) — current mode is registry default (BACKWARD) until proven otherwise | Platform maintainers |
| F-E | Documentation for migrating existing subjects to the new policy without breaking consumers (drain + re-register under FORWARD_TRANSITIVE) | Architecture team / ops |

---

## 8. Cross-References

- [master-design § 3](../eshop-master-design.md) — internal vs external event discipline + Avro style rules (field `doc`, nullable unions, decimals, timestamps, UUIDs)
- [events-catalog.md § 5](events-catalog.md) — full `.avsc` files for every new event / command
- [events-catalog.md § 7](events-catalog.md) — inbox message-type registration per service
- [error-taxonomy.md](error-taxonomy.md) — how deserialization errors route through DLT
- [kafka-dlq-strategy.md](kafka-dlq-strategy.md) — DLT behavior when a consumer can't decode a message (e.g., missing Schema Registry entry)
- [`platform/Platform.Avro.UniversalSerDes/UniversalAvroSerializer.cs`](../../platform/Platform.Avro.UniversalSerDes/UniversalAvroSerializer.cs) — existing serializer that registers schemas on first publish
- [`platform/Platform.Test.Framework/Kafka/KafkaTestContainer.cs`](../../platform/Platform.Test.Framework/Kafka/KafkaTestContainer.cs) — existing test setup confirming `SubjectNameStrategy.Record`
- Pending **ADR-0007 (Avro Compatibility Policy)** — to be authored alongside the implementation wave; will supersede any conflicting clause here
