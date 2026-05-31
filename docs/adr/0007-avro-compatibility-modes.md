# ADR-0007: Avro Schema Compatibility Modes

## Status

Accepted (2026-04-18)

## Context

The eShop reference solution uses Confluent Schema Registry to govern Avro schemas for all cross-service Kafka events and commands. Schema Registry supports seven compatibility modes: `NONE`, `BACKWARD`, `BACKWARD_TRANSITIVE`, `FORWARD`, `FORWARD_TRANSITIVE`, `FULL`, `FULL_TRANSITIVE`. The default (`BACKWARD`) is rarely the right choice for a system where producers and consumers evolve on independent deployment cycles.

The existing DotNetAtlas codebase (Weather / Payments / Order sagas) does not yet have an explicit compatibility mode documented. Schemas are registered at first publish with whatever the Schema Registry defaults to. For a reference solution that introduces 23 new schemas across 8 new topics — some carrying audit-critical events with infinite retention — we must make the policy explicit and record the decision.

Two structural differences across topics shape the choice:

1. **Event-log topics** (`catalog.products`, `ordering.orders`, `inventory.*`, `basket.sessions`, `payments.transactions`) have **infinite retention** per the audit-trail requirement documented in [ADR-0006](0006-event-sourcing-for-inventory.md) and events-catalog.md D-8. A consumer that backfills from offset 0 after a schema has gone through N non-breaking changes must still deserialize every historical message correctly.

2. **Command topics** (`ordering.order-commands`, `inventory.reservation-commands`, `payments.payment-commands`) carry short-lived imperative intent (7-day retention) but have BOTH producers and consumers evolving independently. The Checkout saga may deploy a new command schema before Ordering / Inventory upgrades, or vice versa. Both sides must tolerate each other's version drift.

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
  - Applies to: `ordering.order-commands`, `inventory.reservation-commands`, `payments.payment-commands`.
- **Subject naming strategy**: **Record Name Strategy** (already in use by `UniversalAvroSerializer` — see [Platform.Avro.UniversalSerDes](../../platform/Platform.Avro.UniversalSerDes/)). Subject = `{Namespace}.{RecordName}`, e.g., `Catalog.Products.ProductCreatedEvent`.

## Rationale

**Event-log topics drive the transitive requirement.** With infinite retention (per ADR-0006) a consumer backfilling from offset 0 must be able to deserialize a message published 18 months ago under schema v1 using consumer code built against schema v3. Non-transitive `FORWARD` breaks this — it only guarantees v3 is readable by v2 consumers, not by v1 consumers. `FORWARD_TRANSITIVE` guarantees all historical versions remain consumable by the latest consumer code, which is what audit-trail semantics demand.

**Command topics cannot assume deploy order.** The Checkout saga introduces a new command field; Ordering must tolerate it. Ordering updates its consumer to read the new field; the saga must still produce the old shape for 7 days until saga redeploy. Both directions. Non-transitive `FULL` is insufficient for the same reason as events — a three-step evolution can leave v1 ↔ v3 incompatible.

**Record Name Strategy over Topic Name Strategy** is the right choice for two reasons:
1. It already matches the codebase — `UniversalAvroSerializer` derives subjects from `ISpecificRecord.Schema.Fullname`.
2. It allows a single record type to travel across multiple topics with one subject (rare but useful — e.g., `SendEmailNotificationCommand` flows from multiple producers into `notifications.email-commands`).

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

- **Bootstrap (v1 reference)**: `docker-compose.yaml` ships a `schema-registry-init` one-shot companion service (profile `full`) that runs once the registry is healthy. The bootstrap is **dynamic**: it mounts [`platform/Platform.SchemaRegistry.Contracts/Avro/`](../../platform/Platform.SchemaRegistry.Contracts/Avro/) read-only into the container, walks every `.avsc` under it, derives the subject name via Record Name Strategy (`{dir-relative-path-with-dots}.{filename-without-ext}` — matches `UniversalAvroSerializer.Schema.Fullname`), and PUTs the compatibility mode classified by the filename suffix:

  ```bash
  # The actual bootstrap loop (see docker-compose.yaml § schema-registry-init):
  curl -X PUT "$SR/config" -d '{"compatibility":"FORWARD_TRANSITIVE"}'   # global fallback
  find /avro -name '*.avsc' | sort | while read -r f; do
    rel="${f#/avro/}"
    subject="$(printf '%s' "$rel" | sed -e 's|/|.|g' -e 's|\.avsc$||')"
    case "$f" in
      *Event.avsc)   mode='FORWARD_TRANSITIVE' ;;
      *Command.avsc) mode='FULL_TRANSITIVE'    ;;
      *)             echo "ERROR: $f has no Event/Command suffix"; exit 1 ;;
    esac
    curl -fsS -X PUT "$SR/config/$subject" -d "{\"compatibility\":\"$mode\"}" \
      || echo "warn: $subject not yet registered"
  done
  ```

  Two consequences:
  1. **The naming convention is the API contract.** Every `.avsc` MUST be named `*Event.avsc` (FORWARD_TRANSITIVE — event log) or `*Command.avsc` (FULL_TRANSITIVE — command stream). A file with any other suffix fails the bootstrap loudly with exit-1. There is no quiet-skip path. This is the enforcement seam for the event-vs-command discipline of [master-design § 3.5](../eshop-master-design.md) and [ADR-0023](0023-payments-event-vs-command-classification.md).
  2. **No hand-maintained subject list.** Renames, additions, and deletions are picked up by the next bootstrap automatically. When `PaymentRequestedEvent.avsc` was deleted and `RequestPaymentCommand.avsc` added per ADR-0023, the bootstrap on next compose-up registered the new subject as `FULL_TRANSITIVE` without code change here. The old `Payments.Transactions.PaymentRequestedEvent` subject lingers in the Registry's history (deleting a subject is a deliberate ops decision, not something this bootstrap does), but no longer receives compat-mode-PUTs.

  Per-subject PUTs against not-yet-published subjects produce a warning (`subject not yet registered`) but do not fail the bootstrap; the global `FORWARD_TRANSITIVE` default already covers them on first publish.

  Production bootstrap should use the same dynamic-discovery payload-shape behind whichever orchestrator owns the cluster (Helm hook, ArgoCD pre-sync, Terraform null_resource, etc.). Mounting the `.avsc` set into the bootstrap container — or shipping it baked-in as a values-file — is the pattern.

- **Global fallback**: the `schema-registry-init` bootstrap sets the Registry global compatibility to `FORWARD_TRANSITIVE` as the conservative default for any future subject that lacks explicit config.

- **CI gate**: add a `schema-compat-check` step that, for each `.avsc` file changed in a PR, runs a simulated register against the current Registry; fails the build on incompatibility. The Event/Command suffix gate runs at bootstrap time but should also run pre-merge to catch suffix violations before they hit the registry.

- **Breaking-change process** (additions only — applies WITHIN a subject):
  1. Add a new field with a default. Compatible. Safe to deploy.
  2. If removal is needed: mark as deprecated in `doc`, introduce the replacement field, deploy producer that populates both. Consumers migrate to reading the replacement.
  3. The deprecated field is NEVER physically removed from the subject (transitive constraint). A "major version" is a new subject with a new name, not a breaking mutation of an existing one.

- **Renames are new subjects.** Renaming a record (e.g., `PaymentRequestedEvent` → `RequestPaymentCommand` per ADR-0023) produces a new subject under Record Name Strategy and orphans the old one. In a non-production reference repo, hard cutover is acceptable (delete old `.avsc`, add new). In production, see ADR-0023's discussion of parallel-publish vs hard-cutover trade-offs.

## Related Decisions

- [ADR-0001: Centralized Saga Orchestration](0001-centralized-saga-orchestration.md) — the saga publishes commands; FULL_TRANSITIVE on command topics is what makes saga-service deploy independence work.
- [ADR-0006: Event Sourcing for Inventory](0006-event-sourcing-for-inventory.md) — the infinite retention requirement on event-log topics is what makes FORWARD_TRANSITIVE necessary.
- [avro-compatibility.md](../bc-design/avro-compatibility.md) — operational companion document: per-topic table, breaking-change process, developer workflow.
