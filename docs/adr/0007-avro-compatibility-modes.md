# ADR-0007: Avro Schema Compatibility Modes

## Status

Accepted (2026-04-18)

## Context

The eShop reference solution uses Confluent Schema Registry to govern Avro schemas for all cross-service Kafka events and commands. Schema Registry supports seven compatibility modes: `NONE`, `BACKWARD`, `BACKWARD_TRANSITIVE`, `FORWARD`, `FORWARD_TRANSITIVE`, `FULL`, `FULL_TRANSITIVE`. The default (`BACKWARD`) is rarely the right choice for a system where producers and consumers evolve on independent deployment cycles.

The existing DotNetAtlas codebase (Weather / Payments / Order sagas) does not yet have an explicit compatibility mode documented. Schemas are registered at first publish with whatever the Schema Registry defaults to. For a reference solution that introduces 23 new schemas across 8 new topics — some carrying audit-critical events with infinite retention — we must make the policy explicit and record the decision.

Two structural differences across topics shape the choice:

1. **Event-log topics** (`catalog.products`, `ordering.orders`, `inventory.*`, `basket.sessions`, `payments.transactions`) have **infinite retention** per the audit-trail requirement documented in [ADR-0006](0006-event-sourcing-for-inventory.md) and events-catalog.md D-8. A consumer that backfills from offset 0 after a schema has gone through N non-breaking changes must still deserialize every historical message correctly.

2. **Command topics** (`ordering.order-commands`, `inventory.reservation-commands`, `payments.commands`) carry short-lived imperative intent (7-day retention) but have BOTH producers and consumers evolving independently. The Checkout saga may deploy a new command schema before Ordering / Inventory upgrades, or vice versa. Both sides must tolerate each other's version drift.

This ADR specifies the per-topic-category compatibility policy and the breaking-change process.

## Decision Drivers (ranked)

1. **Historical-message deserialization** — for infinite-retention topics, every schema version ever published must remain readable by every consumer after future evolutions.
2. **Independent deploy cadence** — services ship on different schedules; schema incompatibility must not block any single deploy.
3. **Contract clarity** — schema evolution rules should be obvious to any engineer inspecting a `.avsc` change in a PR.
4. **Enforceability** — Schema Registry must reject incompatible schemas at register time; CI must run the same check before merge.
5. **Consistency with existing convention** — align with the codebase's Record-Name subject strategy already in use by `UniversalAvroSerializer`.

## Considered Options

### Option 1: `FORWARD_TRANSITIVE` for events + `FULL_TRANSITIVE` for commands (chosen)

- Event topics: producers ship new schemas; old consumers can keep reading (new fields have defaults, deletions forbidden).
- Command topics: both directions compatible across ALL historical versions — old producer → new consumer AND new producer → old consumer both work.
- Transitive variants enforce the guarantee across every prior version, not just the immediately preceding one.

### Option 2: `FORWARD` + `FULL` (non-transitive)

- Only the adjacent version is checked. A three-step evolution (v1 → v2 → v3) might leave v1 consumers unable to read v3 messages even though v1↔v2 and v2↔v3 are each compatible.
- Insufficient for infinite-retention event logs.

### Option 3: `BACKWARD` for everything (the Schema Registry default)

- Consumers deploy first; producers follow. Well-suited to tightly-coupled consumer-first teams.
- Fails this solution's "independent deploy cadence" driver: the Checkout saga and the services it commands are different deployment units with no agreed "who-first" rule.

### Option 4: `NONE` (no enforcement)

- No check at register time; rely on reviewer discipline.
- Unacceptable — one missed PR review can silently break every downstream consumer and every historical replay.

## Evaluation Matrix

| Driver (ranked) | Option 1: FWD/FULL TRANSITIVE | Option 2: FWD/FULL non-transitive | Option 3: BACKWARD | Option 4: NONE |
|----|----|----|----|----|
| 1. Historical deserialization | ✅ guaranteed across all versions | ⚠️ only adjacent versions | ✅ consumers ahead | ❌ no guarantee |
| 2. Independent deploys | ✅ producer or consumer may lead | ✅ same | ⚠️ consumer-first only | ✅ no gate at all |
| 3. Contract clarity | ✅ add-with-default-only is easy to explain | ✅ same | ⚠️ "remove fields only" is less intuitive | ❌ no rule |
| 4. Enforceability | ✅ Registry + CI reject incompatible | ✅ same | ✅ same | ❌ bypassable |
| 5. Existing convention | ✅ pairs with Record-Name subject strategy | ✅ same | ✅ same | ✅ same |

## Decision

- **Event-log topics** → `FORWARD_TRANSITIVE`
  - Applies to: `catalog.products`, `catalog.categories`, `basket.sessions`, `ordering.orders`, `inventory.stock-events`, `inventory.reservations`, `payments.transactions`.
- **Command topics** → `FULL_TRANSITIVE`
  - Applies to: `ordering.order-commands`, `inventory.reservation-commands`, `payments.commands`.
- **Subject naming strategy**: **Record Name Strategy** (already in use by `UniversalAvroSerializer` — see [Platform.Avro.UniversalSerDes](../../platform/Platform.Avro.UniversalSerDes/)). Subject = `{Namespace}.{RecordName}`, e.g., `Catalog.Products.ProductCreatedEvent`.

## Rationale

**Event-log topics drive the transitive requirement.** With infinite retention (per ADR-0006) a consumer backfilling from offset 0 must be able to deserialize a message published 18 months ago under schema v1 using consumer code built against schema v3. Non-transitive `FORWARD` breaks this — it only guarantees v3 is readable by v2 consumers, not by v1 consumers. `FORWARD_TRANSITIVE` guarantees all historical versions remain consumable by the latest consumer code, which is what audit-trail semantics demand.

**Command topics cannot assume deploy order.** The Checkout saga introduces a new command field; Ordering must tolerate it. Ordering updates its consumer to read the new field; the saga must still produce the old shape for 7 days until saga redeploy. Both directions. Non-transitive `FULL` is insufficient for the same reason as events — a three-step evolution can leave v1 ↔ v3 incompatible.

**Record Name Strategy over Topic Name Strategy** is the right choice for two reasons:
1. It already matches the codebase — `UniversalAvroSerializer` derives subjects from `ISpecificRecord.Schema.Fullname`.
2. It allows a single record type to travel across multiple topics with one subject (rare but useful — e.g., `SendEmailNotificationCommand` flows from multiple producers into `notification.commands`).

**The decision is enforced at two gates**: Schema Registry rejects incompatible registration at first publish; CI runs a pre-merge `schema-registry-compatibility-check` step that validates every `.avsc` change in a PR against the registry.

## Consequences

### Positive

- Full backward readability across every historical version on event-log topics → reliable audit-trail replay, reliable ES projection rebuild.
- Full bidirectional compatibility on command topics → saga and services can deploy in any order.
- Explicit, enforced contract rules eliminate "this deployed fine locally but blew up in staging" Avro surprises.
- Record Name Strategy means schemas are named by their content, not by coincidental topic placement — a clean conceptual model.

### Negative

- `FORWARD_TRANSITIVE` forbids removing fields entirely (historical messages with the field must remain deserializable by current code). Field deprecation must be multi-phase: add `@deprecated` doc → retain field forever OR register a new major-version subject.
- `FULL_TRANSITIVE` adds the same no-removal constraint to commands — even though command retention is only 7 days, the transitive compatibility check examines ALL historical versions regardless of message lifetime.
- A breaking change genuinely requires a new subject name (`ProductCreatedEventV2`), dual-publish migration period, and eventual cutover — heavier than a simple in-place rename.
- Developers must understand the compatibility mode; onboarding docs must cover it (this ADR + [avro-compatibility.md](../bc-design/avro-compatibility.md)).

### Risks

- **Accumulation of deprecated fields** — over many years, schemas accumulate fields no one uses. Mitigation: document deprecations in field `doc`; periodic major-version schema rotation with consumer migration.
- **Registry misconfiguration** — if the compatibility mode is not explicitly set in the Registry bootstrap, the default (`BACKWARD`) applies. Mitigation: provision Schema Registry with explicit per-subject modes at infra bootstrap (see implementation notes).
- **Consumer drift** — a consumer that has not been redeployed in many months may have code that can't yet read a very new field (even though the Avro layer succeeds — the field is defaulted to null). Mitigation: semantic contracts treat all new fields as optional enrichment; critical data never arrives as "new field only".

## Implementation Notes

- **Bootstrap (v1 reference, provisioned Wave 1.5 cross-cutting)**: `docker-compose.yaml` ships a `schema-registry-init` one-shot companion service (profile `full`) that runs once the registry is healthy and PUTs the global `FORWARD_TRANSITIVE` default plus per-subject overrides for every event/command subject listed under §Decision. Subjects use Record Name Strategy. Per-subject PUTs against not-yet-published subjects log a warning but do not fail the bootstrap; the global default already covers them on first publish.

  ```bash
  # For every event-log subject (FORWARD_TRANSITIVE, generated by schema-registry-init)
  curl -X PUT http://schema-registry:8081/config/{Subject} \
       -H "Content-Type: application/vnd.schemaregistry.v1+json" \
       -d '{"compatibility": "FORWARD_TRANSITIVE"}'

  # For every command subject (FULL_TRANSITIVE)
  curl -X PUT http://schema-registry:8081/config/{Subject} \
       -H "Content-Type: application/vnd.schemaregistry.v1+json" \
       -d '{"compatibility": "FULL_TRANSITIVE"}'
  ```

  Production bootstrap should use the same payloads via the Registry REST API behind whichever orchestrator owns the cluster (Helm hook, ArgoCD pre-sync, Terraform null_resource, etc.).

- **Global fallback**: the `schema-registry-init` bootstrap sets the Registry global compatibility to `FORWARD_TRANSITIVE` as the conservative default for any future subject that lacks explicit config.

- **CI gate**: add a `schema-compat-check` step that, for each `.avsc` file changed in a PR, runs a simulated register against the current Registry; fails the build on incompatibility.

- **Breaking-change process** (additions only):
  1. Add a new field with a default. Compatible. Safe to deploy.
  2. If removal is needed: mark as deprecated in `doc`, introduce the replacement field, deploy producer that populates both. Consumers migrate to reading the replacement.
  3. The deprecated field is NEVER physically removed from the subject (transitive constraint). A "major version" is a new subject with a new name, not a breaking mutation of an existing one.

## Related Decisions

- [ADR-0001: Centralized Saga Orchestration](0001-centralized-saga-orchestration.md) — the saga publishes commands; FULL_TRANSITIVE on command topics is what makes saga-service deploy independence work.
- [ADR-0006: Event Sourcing for Inventory](0006-event-sourcing-for-inventory.md) — the infinite retention requirement on event-log topics is what makes FORWARD_TRANSITIVE necessary.
- [avro-compatibility.md](../bc-design/avro-compatibility.md) — operational companion document: per-topic table, breaking-change process, developer workflow.
