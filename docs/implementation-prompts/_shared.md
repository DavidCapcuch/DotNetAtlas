# Shared Implementation Guidance

> **Read this FIRST**, before the BC-specific prompt or any design docs. This is the common operating manual for every BC implementation agent in the eShop reference solution. Read once at session start; re-open whenever uncertain.

## 1. Who you are + the setup

You implement **one bounded context (or one cross-cutting wave)** of the DotNetAtlas eShop. You work in a fresh Claude Code session at the root of your local DotNetAtlas clone. You coordinate with agents in other BCs ONLY through the version-controlled design docs. The unit-specific prompt tells you which folder is yours and what's off-limits.

**Spec-driven discipline:** Contracts at the seams (events, topics, Avro schemas, cross-BC calls) are **LOCKED**. Everything inside your unit (code structure, specification classes, validator mechanics, error-class API, test-split depth, tooling choices) is **OPEN** — you design it, justify it in the session summary. (Per [Anthropic's harness-design guidance](https://www.anthropic.com/engineering/harness-design-long-running-apps), the scope + verification contract is agreed *before* implementation — this kit is that contract.)

**Build status + the wave model.** Wave 0 (platform) + Wave 1 (Catalog ∥ Basket ∥ Ordering ∥ Inventory ∥ Payments ∥ Invoicing, plus **Notifications** added later) + Wave 2 (the **Checkout** and **Payments-processing** sagas, under `saga/SagaOrchestrators/`) are **built** — seven convention-current BCs in `services/`. The one remaining dispatch is **Wave 3 — the BFF** (`bff.md`), this kit's live exemplar. The wave model still matters as the **file-ownership discipline** that kept parallel sessions conflict-free: each unit owns disjoint `services/{BC}/**` (or `src/EShop.BFF/**`) and touches shared seams only through the design docs.

`_template.md` codifies the canonical prompt structure (XML-tagged sections + `<thinking_first>`, `<stop_conditions>`, `<session_management>`, `<verification>`, `<peer_review>`). Every dispatch prompt follows it; § 7 below is the lifecycle those prompts run.

## 2. Canonical reading order

On top of this file, every BC reads (in order):

1. `CLAUDE.md` + `.claude/rules.md` — repo rules and standing constraints; `.claude/verification-gates.md` for the gate deltas
2. `docs/eshop-master-design.md` — **especially § 3 event discipline, § 5 BC overview (find your BC), § 6 Kafka topics, § 10 diagrams, § 11 cross-cutting**
3. `docs/eshop-general-plan.md` — solution tree
4. `docs/adr/` — **read every ADR your `<applicable_adrs>` block names; skim the rest from the directory** (don't trust a hardcoded count — the set currently spans `0001`–`0033`, with `0030` a tombstone). 0001–0007 domain decisions, 0008 a deprecated stub, 0009+ cross-cutting (service-auth, PII, versioning, idempotency, feature flags, time, Redis, blob, invoice numbering, PDF, **0024 dispatch-in-interceptor**, **0029 order-keyed saga**, **0033 topic-contract SSOT**, …). The directory is the source of truth.
5. Your BC's chapter + glossary + example-mapping under `docs/bc-design/`
6. `docs/bc-design/events-catalog.md` + `use-cases.md` — find rows for your BC
7. `docs/bc-design/error-taxonomy.md`, `kafka-dlt-strategy.md`, `architecture-tests.md` (Avro schema compatibility is now [ADR-0007](../adr/0007-avro-compatibility-modes.md) + `kafka-topology.md`; `avro-compatibility.md` is a redirect stub per ADR-0033 — don't read it)
8. Saga-affected BCs also read: `saga-stuck-runbook.md`. BFF reads: `rate-limiting.md`. Invoicing reads: ADR-0017/0018/0019.
9. `docs/diagrams/context-map.md` + `bc-map-entities.md` (mermaid sources)

## 3. Stack conventions

.NET 10 microservices:

- **ASP.NET Core** + **FastEndpoints** (HTTP) — version pinned in `platform/Directory.Packages.props`
- **EF Core** + **PostgreSQL** (`Npgsql.EntityFrameworkCore.PostgreSQL`), snake_case via `EFCore.NamingConventions`
- **KafkaFlow** for Kafka consumers/producers
- **Confluent Schema Registry** + **Avro** via `Platform.Avro.UniversalSerDes`
- **FluentValidation** (validators), **FluentResults** (`Result` / `Result<T>`), **Ardalis.SmartEnum**, **Ardalis.Specification.EntityFrameworkCore**, **Riok.Mapperly**
- **Redis** (split: `redis-basket` AOF for Basket primary; `redis-cache` volatile for BFF backplane + ASP.NET Output Cache backing per ADR-0013/0016) + **FusionCache** (Basket primary store, Inventory stock read-through cache per ADR-0034, BFF composed cache)
- **MassTransit** + EF Core saga persistence (saga/ only)
- **OpenTelemetry** + OTLP collector; Seq dev sink
- **Azure.Storage.Blobs** + **Azurite** (local) for blob storage (Invoicing)
- **OpenFeature** + JSON-file provider for feature flags (per ADR-0014)
- **Keycloak** OAuth2 (user auth + service-account client-credentials for service-to-service per ADR-0010)
- **xUnit** + **Testcontainers**
    - xunit.v3 enforces `xUnit1051` — pass `TestContext.Current.CancellationToken` to every async call in a test body, otherwise the build fails.
    - ASP.NET middleware tests built on `DefaultHttpContext` do **not** fire `Response.OnStarting` callbacks (the callbacks are registered but never invoked without a real pipeline). Either set response headers eagerly, or stand up `Microsoft.AspNetCore.TestHost` for the assertion.

**Schema application, and how to generate a migration** — the three-strategies-by-environment split (dev `MigrateAsync` / test Evolve / deployed Flyway), the `dotnet ef` invocations, and why the SQL script needs both `--idempotent` and `--no-transactions`: [`.claude/migrations.md`](../../.claude/migrations.md). The consequence that matters here: **test fixtures never call `MigrateAsync` / `EnsureCreatedAsync`** — they exercise the exact SQL prod runs.

Do not introduce new libraries without documenting rationale + asking. When an added package **is** approved: add it to the correct-level `Directory.Packages.props`, run `dotnet restore` once **without** `--locked-mode` to regenerate `packages.lock.json`, then commit the lock delta — otherwise the locked-mode gate fails on the next restore.

## 4. Golden reference — read a real built BC, don't mirror by eye

Seven convention-current BCs exist. **Read the closest one for the shape; copy the structure, not the domain.** Mirroring-by-eye is what let cross-BC consistency drift; the rules below, not a file list, are the source of truth.

**One exception — test-project structure:** the built BCs' test-project layout is not yet the target taxonomy, so take the test projects from [master design § 11.4](../eshop-master-design.md) (`{Bc}.UnitTests` / `.IntegrationTests` / `.ArchitectureTests`), not from the closest built BC.

| Your unit's shape | Golden reference | Why it's the model |
|---|---|---|
| Standard 4-layer, EF Core + outbox (default) | **Catalog** (`services/Catalog/`) | Clean aggregate BC: domain events, projections, `DispatchDomainEventsInterceptor`, FastEndpoints, the 3 test projects |
| Event-sourced aggregate | **Inventory** (`services/Inventory/`) | `IEventStore` + append-only `EventStoreRepository`; domain-event dispatch inside `AppendAsync` |
| Redis-primary aggregate | **Basket** (`services/Basket/`) | Redis-backed persistence, dispatch in handler after `SaveAsync`, `ProductCatalog` ACL HTTP adapter |
| PII / external gateway | **Payments** (`services/Payments/`) | `_enc` column convention (ADR-0011), outbox-only publish path (arch-tested) |
| 2-layer aggregation gateway | **BFF** (`bff.md` in this kit) | Stateless composition, typed clients, FusionCache; no domain model |

**The shape is the SSOT, not the paths.** Conventions are canonical in `docs/bc-design/conventions.md` (its §8 map points at the authoritative doc per topic), and the executable rules are in `docs/bc-design/architecture-tests.md` (NetArchTest, CI-blocking). **A change that would fail an arch test is a real failure, not a style nit.**

## 5. Platform libraries (consume, don't modify — Wave 0 extends `ServiceDefaults`)

- `Platform.SharedKernel` — `Entity<TId>`, `AggregateRoot<TId>`, `ValueObject`, `DomainEvent`, `DataIntegrityException`, **`Money`**, **`Address`** (Wave 0 additions). Time abstraction is BCL `System.TimeProvider` (auto-registered by Generic Host per [ADR-0015](../adr/0015-time-timezone-policy.md)); no custom `IClock` interface — inject `TimeProvider` directly, use `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing` in tests.
- `Platform.CQRS` — `ICommand[<T>]`, `IQuery<T>`, handler + dispatcher + behaviour chain (Tracing → Logging → Metrics → Validation → Handler)
- `Platform.ReliableMessaging.Outbox.EFCore` — `ITransactionalOutbox<TDbContext>.AddOutboxMessage(topic, key, ISpecificRecord)`
- `Platform.ReliableMessaging.Inbox.EFCore` — `AddInbox<TDbContext>(typeof(...))` for Kafka dedup
- `Platform.KafkaFlow.DeadLetter` — `.DLT` suffix convention (see `kafka-dlt-strategy.md`)
- `Platform.KafkaFlow.Inbox.EFCore`, `Platform.KafkaFlow.ProducerHeaders` — inbox dedup + producer headers (`message.id`, `origin`)
- `Platform.Avro.UniversalSerDes` — Record-Name-Strategy subjects
- `Platform.SchemaRegistry.Contracts` — all `.avsc` files live here
- `Platform.OutboxRelay.WorkerService` — you ADD a container per service schema in docker-compose
- `Platform.ServiceDefaults` — OTel + health checks + problem details + **Wave 0 additions: OAuth2 service-auth, OpenFeature DI**. Cross-service HTTP resilience is handled by YARP at the edge — no per-service Polly presets are shipped.
- `Platform.Test.Framework` — shared test fixtures

If you hit a gap, **ASK** — adding platform code is an escalation.

## 6. Async vs Sync — short form

Full matrix: master-design § 11.7. Short form:

| Scenario | Transport |
|---|---|
| BFF or another service reads your state for a user-blocking request | **HTTP** |
| Inside-request cross-BC enrichment via ACL | **HTTP via adapter** |
| Business moment other BCs may react to | **Kafka via outbox** |
| Saga orchestrates multiple BCs | **Kafka commands + events** |
| Fire-and-forget side effect (notifications, audit) | **Kafka** |
| Admin one-shot, operator needs feedback | **HTTP** |
| Cross-aggregate strict consistency | **STOP — rethink the aggregate boundary** |

Forbidden: sync HTTP across a saga-coordinated boundary; publishing external events without the outbox; consuming another BC's internal `*DomainEvent`; 3-service two-way sync chain.

**When in doubt → ask.**

## 7. The dispatch lifecycle + the skills at each phase

A dispatch runs the same lifecycle whether the unit is a BC or a cross-cutting wave. Invoke each skill proactively at its trigger; the BC-specific prompt extends this with any unit-specific skills.

| Phase | Skill | Trigger |
|---|---|---|
| Session start | `superpowers:using-superpowers` | auto — establishes how you find and use skills |
| **0 — sharpen the design** | `grill-with-docs` | before writing the spec/code: stress-test the unit's design against its `bc-design` chapter, glossary, and the applicable ADRs; sharpen terminology, kill ambiguity. (Replaces a generic brainstorm — it grounds the open-interior design in the documented model.) |
| **1 — decompose** | `to-spec` → `to-tickets` | turn the sharpened design into a spec, then into independently-grabbable **tracer-bullet** tickets (thin vertical slices, each demoable end-to-end). Decomposition lives here, not in a separate roadmap tool. |
| **2 — dispatch** | this kit (`_template.md` / the unit's prompt) | the spec the build session executes |
| **3 — build loop** | `tdd` | red → green → refactor per **behaviour** (not per step); deep-module + interface-design discipline. Each test responds to what the previous cycle taught you. |
| When stuck | `superpowers:systematic-debugging` | unexpected behaviour, flaky test, inconsistent reproduction |
| **Pre-commit, every milestone (≥ 5 files)** | `Agent(subagent_type="feature-dev:code-reviewer", model="opus")` | **mandatory** correctness/quality review before staging — brief with the exact file list + test list + what's intentionally deferred. Validated precedent: caught one CRITICAL + three IMPORTANT findings that would otherwise have shipped. Use `opus` explicitly; the default model is weaker. |
| **4 — gate** | `superpowers:verification-before-completion` | evidence-first: run every `<verification>` command and paste the **actual output** (not a summary) before any "done" claim |
| **5 — DoD gate (final step)** | `daca-dod-reviewer` (+ `daca-bc-consistency-reviewer` / `daca-documentation-reviewer`) | self-attest the **Self-attested** bucket of `daca-dod-reviewer`'s bar in your summary, then run `daca-dod-reviewer` on your diff: an applicability-gated audit of the **Reviewer-audited** bucket that **delegates Architecture/DDD to `daca-bc-consistency-reviewer`** (golden-BC drift vs §4 + `conventions.md` + `architecture-tests.md`, judgment dimensions: DI/decorator order, error-factory placement, outbox-dispatch path, topic topology, options shape, persistence layout, test-split) **and Documentation to `daca-documentation-reviewer`** (which carries the doc-style bar). Objective violations block; judgment concerns warn. |
| .NET idioms | `dotnet-contribution:dotnet-backend-patterns` | continuous — C#/.NET pattern reference |

> **Optional quality check:** a feature-scoped mutation-testing pass (target ≥ 80% kill) is a good sanity-check on test strength after green — recommended, not a gate.
>
> **Code-review craft:** for *how* to ask for and act on review, `superpowers:requesting-code-review` / `receiving-code-review` are available; they're craft guidance, not gates.

## 8. Autonomous evolution protocol

You are not a transcription machine. Read critically, evolve when the code reveals gaps.

1. **Flag inconsistencies between docs.** If `catalog.md` says 10 commands and `use-cases.md` lists 12, STOP and report. Do not silently pick one.
2. **Propose improvements.** If you spot a cleaner shape, add a "Proposed improvement" note to the session summary. Implement ONLY what the design specifies, not what you prefer, unless the user approves the proposal.
3. **Self-correct the domain model** when example mapping or integration tests surface a missing rule:
   - Update `docs/bc-design/{bc}.md` with the new invariant + one-line rationale
   - Add a session to `docs/bc-design/example-mapping/{bc}.md` (Given/When/Verify/Then)
   - THEN implement — doc-first keeps review reviewable
4. **Ask before non-obvious tradeoffs.** HTTP vs Kafka; optimistic vs pessimistic concurrency; sync vs async projection. Silent guessing is worse than a 60-second pause to confirm.
5. **Session summary is mandatory.** Files created, decisions taken (with rationale), inconsistencies found (file:line), improvements proposed (unimplemented until approved), domain-model self-corrections, open questions.

## 9. Universal stop conditions

Beyond the BC-specific stop conditions in your prompt's `<stop_conditions>`, every agent stops and asks the user when:

- A file referenced in `<reading_order>` does not exist or is empty.
- A prerequisite your dispatch depends on isn't actually present in the repo — e.g. the platform foundations (service-auth, redis-basket / redis-cache split, Azurite container) or an upstream BC's HTTP surface or events.
- An ADR contradicts a BC design doc.
- A `<contract>` item conflicts with `events-catalog.md` or `use-cases.md` (file:line).
- An open design decision in `<design_open>` has implications you didn't expect — flag the trade-off, name your tentative choice, ask before committing.
- You're about to introduce a new platform library or NuGet package not listed in `<reading_order>` or this `_shared.md` § 3.
- You're about to skip a step in the dispatch sequence (e.g., starting Wave 2 before Wave 1 is verified).
- Your context window approaches 80% full — stop, summarise, and suggest using /handoff skill (see § 10).

## 10. Session management

Every BC implementation is multi-file, multi-hour work. Manage the session as you would a long PR:

- **Commit in logical milestones, not per-file.** Suggested chunks: scaffold + unit-test scaffolding (1 commit) → domain layer + unit tests (1 commit) → application layer + handlers + outbox publishers (1 commit) → infrastructure layer + DI + Kafka consumers (1 commit) → integration tests (1 commit) → architecture tests (1 commit) → docker-compose delta (1 commit) → docs self-corrections (1 commit). Tune to your BC's shape.
- **Test before committing.** After each milestone, run the relevant test slice from `<verification>`. Do not accumulate untested work — `dotnet test` failures debugged after 5 commits is much harder than after 1.
- **Surface progress.** After each milestone, summarise to the user: "Completed `<dod_item>`; tests green; moving to `<next_item>`." This lets the user catch direction problems before more work compounds.
- **Context-window discipline.** When approaching 80% full (≈ 30 large files read), stop, summarise, and ask whether to continue or hand off the remainder to a follow-up session with a context-summary.
- **Emit a handoff prompt at every milestone boundary.** After a milestone commit lands and before ending the session, print the block from `docs/implementation-prompts/_handoff-template.md` with `{BC}` and `{N+1}` substituted for the next milestone. The user pastes it into a fresh session to continue.

## 11. The review stack — three roles

Three review touchpoints ([Anthropic's harness-design guidance](https://www.anthropic.com/engineering/harness-design-long-running-apps): the agent that *builds* should be separate from the agent that *judges* — self-evaluation lets a model confidently praise its own mediocre work). Role 1 is a high-frequency per-milestone sweep; Roles 2–3 are the final gate. Role 1 overlaps Role 3 on quality/correctness **by design** — its value is *frequency* (catching a bug at milestone 3, not milestone 9), not a distinct concern.

**Role 1 — per-milestone correctness sweep (≥ 5 files).**
Before `git commit` on any milestone touching ≥ 5 files, invoke `Agent(subagent_type="feature-dev:code-reviewer", model="opus")`. It's a generic reviewer with **no repo context**, so brief it: the exact file list + test list + design decisions + what's intentionally deferred, **plus a two-line repo preamble** — "Golden reference: `_shared.md § 4`. Outbox-only event seams, result-pattern-not-exceptions, and layer boundaries are arch-tested (`architecture-tests.md`) — flag any violation as a real failure." Fix all CRITICAL/HIGH before staging; document accepted MEDIUM/LOW in the commit body. Use `model="opus"` — precedent surfaced one CRITICAL + three IMPORTANT on a single review; the default is weaker. (The `Agent` tool honours the `model` override; if a harness ever ignores it, have the reviewer state the model it actually ran in its output so the opus assumption isn't silently lost.)

**Role 2 — the gate (before any "done" claim).**
Run the gates via the `daca-gates` skill (this repo's deltas: [`.claude/verification-gates.md`](../../.claude/verification-gates.md)) and paste the **actual pass/fail output — not a summary** — into your session summary, then invoke `superpowers:verification-before-completion` (its checklist catches the "I claimed done but never ran X" gap). The gates are **non-negotiable exit conditions** — no "done" without all of them green and pasted.

**Role 3 — the DoD gate, final step.**
First, **self-attest the `## Self-attested` bucket of `daca-dod-reviewer`'s bar** in your session summary (clarified assumptions, divergent pass run, existing patterns evaluated first, gates green) — a reviewer subagent can't see the conversation, so only you can confirm these.

Then run the DoD audit. **You — the main session — orchestrate it.** In standard Claude Code a subagent can't spawn its own subagents, so the reliable path is to invoke the reviewers as **siblings** from here and aggregate their findings: **`daca-dod-reviewer`** (audits the `## Reviewer-audited` bucket, applicability-gated), **`daca-bc-consistency-reviewer`** (Architecture/DDD golden-BC drift vs § 4 + `conventions.md` + `architecture-tests.md`, ADR-adherence folded in), and **`daca-documentation-reviewer`** (docs vs the doc-style bar it carries). Each reviewer walks its own rubric **inline**. Objective DoD violations block; judgment concerns warn. Fix every blocker before declaring DoD met.

> **Opt-in accelerator (same findings, faster — not required):** if your harness lets a subagent spawn its own, `daca-dod-reviewer` self-delegates to the other two and `daca-bc-consistency-reviewer` fans its seven dimensions out in true parallel. Absent that, sibling-inline is the default.

## 12. Shared Definition of Done — dispatch-structural

The **structural deliverables** unique to a dispatch. The general quality bar is `daca-dod-reviewer`'s (audited at Role 3); the executable gates are `daca-gates`' (Role 2). **Don't restate their items here** — timestamps, route versioning, new-behaviour-tests, and the gate commands live there, not in this list.

- [ ] 4-layer project (`Api`, `Application`, `Domain`, `Infrastructure`) compiles; BFF has 2 layers only (`Api`, `Infrastructure`); sagas have none (orchestrators only, under `saga/SagaOrchestrators/{Checkout,Payments}/`).
- [ ] All commands + queries from `use-cases.md § {your BC}` implemented
- [ ] All internal `*DomainEvent` types declared in Domain layer
- [ ] All external `*Event` Avro schemas created under `platform/Platform.SchemaRegistry.Contracts/Avro/{Domain}/{Aggregate}/`
- [ ] Outbox publishers map internal → external per BC chapter
- [ ] DbContext + naming conventions scaffolded (migration generated per § 3 above, never hand-written)
- [ ] Messaging DI: outbox, inbox, Kafka consumers per BC
- [ ] docker-compose delta: topics + outbox-relay-{bc} container
- [ ] The unit's test projects exist + pass; architecture tests enforce the rules in `architecture-tests.md § {your BC}`
- [ ] Docs self-corrected if needed
- [ ] **Quality bar cleared** — `daca-dod-reviewer`'s Reviewer-audited bucket has no open blockers (Role 3), Self-attested bucket attested, all `daca-gates` gates green with pasted output (Role 2)
- [ ] Review stack (§ 11) run end-to-end (Role 1 → Role 2 → Role 3)
- [ ] Session summary posted

**Not "done" if** — code is uncommitted/untested or the container never started; you silently deviated from the BC chapter; a listed external event lacks its schema file + outbox publisher; a command's validator is handwaved with no documented reason; docs disagree with the (correct) code; you claimed green without pasting the gate output; or an ADR was skipped without a written rationale.
