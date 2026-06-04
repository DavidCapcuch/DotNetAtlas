# ADR-0033: Single Source of Truth for Kafka topic & event-contract documentation

## Status

Accepted (2026-06-04)

## Context

The eShop's Kafka topic / event-contract / DLT / schema-compatibility facts were re-stated in at least six editable Markdown locations:

- [events-catalog.md](../bc-design/events-catalog.md) — §2 master event table, §3 topic→event inverse, §3.1 consumer-group spans, §4 docker-compose delta.
- [kafka-topology.md](../kafka-topology.md) — per-topic partitions / retention / class / compat.
- [kafka-dlt-strategy.md](../bc-design/kafka-dlt-strategy.md) — §3 per-(consumer-BC × source-topic) DLT table (with a duplicated source-retention column).
- [avro-compatibility.md](../bc-design/avro-compatibility.md) — §2.3 per-subject compatibility table.
- [eshop-master-design.md](../eshop-master-design.md) — §6 system-wide producer/consumer summary.
- The per-BC `bc-design/*.md` files — each restating its own topics/events (incl. `notifications.md`'s self-contained topology table, whose pointer-ification is deferred to the v2 code switch, #312/#318).

These overlapped and drifted apart. A single Notifications contract rename (`SendEmailNotificationCommand` → `NotifyUserCommand`, topics `notifications.email-*` → `notifications.notify-*`) touched ~14 files and still left contradictory leftovers; the intra-`events-catalog` drift alone (§2 vs §3.1) needed three audit passes (issue #299). The same fact lived in too many editable places, so any change had to be applied N times and inevitably wasn't.

Two structural facts shape the fix:

1. **The facts split cleanly by grain.** Producer / consumers / consumer-group / correlation-key / trigger / schema-path are **per-event**. Partitions / retention / class are **per-topic**. Compatibility mode is **derived** (the `.avsc` filename suffix → topic class → mode, machine-enforced by `schema-registry-init`, see [ADR-0007](0007-avro-compatibility-modes.md)). DLT topic names are **derived** (`{source-topic}.{consumer-bc}.DLT`).
2. **The authoritative runtime sources already exist and are enforced.** `docker-compose.yaml`'s `kafka-create-topic` block creates the topics with their partitions/retention; `schema-registry-init` sets compat by filename suffix; each BC's `DltTopicSuffix` (appsettings) fixes its DLT names; the live Kafka handler / `AddInbox` registrations fix who consumes what (these were the code ground-truth for #299). The Markdown is downstream of all of this.

## Decision Drivers (ranked)

1. **No fact in more than one editable place** — eliminate the structural cause of drift, not each instance.
2. **Don't regress the in-flight Notifications v2 docs** — `events-catalog §2` + `notifications.md` + [ADR-0031](0031-notify-user-command-and-notification-id.md)/[ADR-0032](0032-notifications-dispatch-and-channels.md) lead the code to v2 deliberately; the built-state docs follow at the code switch (#312/#318) per the docs-don't-lead-built-state policy.
3. **Proportionate to a reference repo** — no heavy toolchain whose only justification is documentation hygiene.
4. **A clean hand-off to the eventual generated catalog** — the long-term end-state is a generated catalog, not hand-maintained Markdown (see Future Direction).

## Considered Options

### Option A — Collapse everything into one anchor (events-catalog §2)

Make `events-catalog §2` the single table and reduce every other doc (including `kafka-topology.md`) to a pointer.

- Rejected: per-topic facts (partitions/retention/class) don't fit a per-**event** table. Folding them in duplicates `3 / -1 / event-log / FORWARD_TRANSITIVE` across every event row sharing a topic (6 `ordering.orders` rows → 6 identical cells) — a *new* intra-table drift surface. Wrong grain.

### Option B — Machine-readable registry + generation

Author a YAML/JSON topic registry; generate the satellite tables; gate with a CI regen-diff so they can't drift by construction.

- Rejected: (a) the hardest-to-keep-correct column — the consumer map + rationale — is editorial design knowledge, not derivable, so the registry would still be hand-maintained for exactly the column that drifts; (b) generating from code would force every doc to the code's current state (v1 `email-*`), reverting the deliberate v2-ahead `events-catalog §2`; (c) a bespoke doc-codegen toolchain is throwaway work, because the real "can't-drift-by-construction" mechanism is the future generated catalog (Future Direction), not a custom generator.

### Option C — Hybrid: two grain-partitioned anchors + eliminate derived facts + pointers (chosen)

Two non-overlapping canonical tables, one per grain; derived facts stated as *rules* rather than tabulated; every other doc points at the anchors.

## Evaluation Matrix

| Driver (ranked) | A — one anchor | B — registry + codegen | C — two-grain hybrid |
|---|---|---|---|
| 1. No duplicated fact | ⚠️ re-duplicates per-topic facts per event row | ✅ by construction | ✅ each fact in exactly one anchor (or derived) |
| 2. No v2 regression | ✅ | ❌ forces docs to code (v1) | ✅ contract anchor stays v2, physical anchor stays v1 |
| 3. Proportionate | ✅ | ❌ codegen + CI gate | ✅ Markdown + pointers only |
| 4. Hands off to generated catalog | ⚠️ partial | ⚠️ competing generator | ✅ two anchors are the hand-off source |

## Decision

**Two grain-partitioned canonical anchors; everything else points; derived facts are never tabulated.**

- **Per-event contract SSOT → [events-catalog.md § 2](../bc-design/events-catalog.md)** ("Master Event Catalog"): event, topic, namespace, producer, consumer(s), consumer-group(s), correlation key, trigger, schema-file path. Stays at the **v2** Notifications contract (`notify-*`).
- **Per-topic physical SSOT → [kafka-topology.md](../kafka-topology.md)**: partitions, retention, class. Stays aligned with the runtime (`docker-compose.yaml`), i.e. **v1** `email-*` until the contract switch lands in code.
- **Compatibility mode is derived, never tabulated.** Filename suffix → topic class → mode, machine-enforced by `schema-registry-init` ([ADR-0007](0007-avro-compatibility-modes.md)). The class→mode rule lives in `kafka-topology.md`; ADR-0007 is the policy. `avro-compatibility.md` (a pre-ADR-0007 companion now subsumed by it) is **retired to a stub pointer**.
- **DLT topic names are derived, never tabulated as data.** `{source-topic}.{consumer-bc}.DLT`; the per-BC `DltTopicSuffix` map and the rule stay in `kafka-dlt-strategy.md §1`; the live pre-created DLT topics live in `docker-compose.yaml`. `kafka-dlt-strategy.md` keeps its unique retry/poison/replay/observability runbook.
- **Every other doc points** at the two anchors instead of restating: `eshop-master-design §6`, the per-BC topic restatements, and `conventions.md`'s topic/compat references.
- **No drift-guard / no doc-generator is added.** The interim Markdown is not the final state (see Future Direction); a bespoke guard or generator would be throwaway. Correctness of the physical facts is already enforced at runtime (compose creates the topics; `schema-registry-init` sets compat or fails loudly).

## Rationale

The grain split is the whole insight: per-event facts and per-topic facts are different tables, so one mega-table can only be built by duplicating the smaller grain. Two anchors keep each fact in exactly one place while matching how the facts are actually shaped. Derived facts (compat, DLT names) are stated once as a *rule* — tabulating them per topic was pure duplication of a deterministic function.

Keeping the two anchors at **different versions during the transition is correct, not a contradiction**: the topic name is the join key between them, and a rename necessarily breaks that join for the renamed topic until both the contract (v2, design-led) and the physical reality (v1 → v2, code-led in #312/#318) converge. Consolidation shrinks a rename from ~14 doc touches to two anchor rows plus the code/compose, and folds those two rows into the contract-switch issue's acceptance criteria.

## Consequences

### Positive

- A topic/contract fact lives in exactly one editable place (or is derived) — the structural drift cause is gone.
- A future rename touches two anchor rows + the runtime, not a dozen prose restatements.
- The §2↔§3.1 transpose that #299 fought is removed (the per-group span bullets are deleted; group membership is read off §2).
- One fewer doc to maintain (`avro-compatibility.md` retired); the self-contradicting topic counts in `master-design §6` are gone.

### Negative

- Two anchors instead of one conceptual "catalog" — a reader wanting both contract and topology consults two tables (linked to each other).
- During the v1→v2 Notifications transition the two anchors disagree on the notifications topic *name* by design; this is marked transitional at both ends and resolved by #312/#318.

### Risks

- A future author could re-introduce a duplicate table (no CI guard). Mitigation: the anchors and this ADR document where each fact lives; review catches re-duplication; the end-state generated catalog removes the temptation entirely.

## Future Direction

The intended end-state is a **generated catalog — Backstage software catalog and/or EventCatalog ([eventcatalog.dev](https://www.eventcatalog.dev/))** — sourced directly from the live SOT and code (the compose topic block, `.avsc` + suffix→compat rule, appsettings `DltTopicSuffix`, the live handler/`AddInbox` registrations). That is a separate future chapter. The interim hand-maintained Markdown here is explicitly **not** the final state; the two anchors are the hand-off source when that chapter begins. This is the reason no bespoke YAML-registry generator (Option B) was built now — it would be superseded.

## Implementation Notes

- `events-catalog.md`: drop §3 (transpose of §2); reduce §3.1 to the one-group-per-service rule + saga exception (delete the per-group span bullets); replace §4's compose delta with a pointer to `docker-compose.yaml` + `kafka-topology.md`.
- `kafka-topology.md`: add the explicit "compat = derived from class" note + a cross-link to §2; mark the `notifications.email-*` rows transitional (→ §2 / ADR-0031 / #312).
- `avro-compatibility.md`: retire to a stub pointing at ADR-0007 + `kafka-topology.md` + `deployment/schema-compat-checks.md`; its evolution anti-patterns fold into ADR-0007.
- `kafka-dlt-strategy.md §3`: drop the source-retention column; reframe the DLT-name column as derived.
- `eshop-master-design §6`, per-BC docs, `conventions.md`: re-point to the two anchors; do not restate.
- Built-state rename (kafka-topology + compose + kafka-dlt §3 + master-design markers → `notify-*`, and pointer-ifying `notifications.md`'s self-contained topology table) is folded into the acceptance criteria of #312 and #318.

## Related Decisions

- [ADR-0007: Avro Schema Compatibility Modes](0007-avro-compatibility-modes.md) — the policy + suffix-driven bootstrap that makes per-subject compat *derived*; the now-retired `avro-compatibility.md` was its companion.
- [ADR-0025: Kafka consumer retry & dead-letter policy](0025-kafka-consumer-retry-dlt-policy.md) — the DLT behaviour documented (not duplicated) in `kafka-dlt-strategy.md`.
- [ADR-0031: NotifyUserCommand & NotificationId](0031-notify-user-command-and-notification-id.md) / [ADR-0032: Notifications dispatch & channels](0032-notifications-dispatch-and-channels.md) — the v2 contract the per-event anchor leads to.
- Issue #299 (intra-`events-catalog` consumer-column drift, closed), #312 / #318 (the Notifications v2 code switch + cleanup that carry the built-state rename).
