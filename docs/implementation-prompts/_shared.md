# Shared Implementation Guidance

> **Read this FIRST**, before the BC-specific prompt or any design docs. This is the common operating manual for every BC implementation agent in the eShop reference solution. Read once at session start; re-open whenever uncertain.

## 1. Who you are + the setup

You implement **one bounded context (or one cross-cutting wave)** of the DotNetAtlas eShop. You work in a fresh Claude Code session at `C:\Users\dcapc\Desktop\Git\DotNetAtlas`. You coordinate with agents in other BCs ONLY through the version-controlled design docs. The BC-specific prompt tells you which folder is yours and what's off-limits.

**Spec-driven discipline:** Contracts at the seams (events, topics, Avro schemas, cross-BC calls) are **LOCKED**. Everything inside your BC (code structure, specification classes, validator mechanics, error-class API, test-split depth, tooling choices) is **OPEN** — you design it, justify it in the session summary.

**The 8-prompt dispatch sequence:**

```
Wave 0 (foundation):  wave-0-platform-prep                                              ← run first, alone
Wave 1 (parallel):    Catalog ∥ Basket ∥ Ordering ∥ Inventory ∥ Payments ∥ Invoicing
Wave 2 (depends 1):   Checkout saga
Wave 3 (depends 1+2): BFF
```

The new template file `_template.md` codifies the canonical prompt structure (XML-tagged sections, `<thinking_first>` directive, `<stop_conditions>`, `<session_management>`, `<peer_review>`). Every BC prompt follows it.

## 2. Canonical reading order

On top of this file, every BC reads (in order):

1. `CLAUDE.md` — repo rules (non-negotiable: locked-mode restore, format gates, no EF migration generation)
2. `docs/eshop-master-design.md` — **especially § 3 event discipline, § 5 BC overview (find your BC), § 6 Kafka topics, § 10 diagrams, § 11 cross-cutting**
3. `docs/eshop-general-plan.md` — solution tree
4. `docs/adr/0001` through `0019` — **all 19 ADRs**. The first 7 are domain decisions; 0008–0019 are cross-cutting (correlation, target profile, service-auth, PII, versioning, idempotency, feature flags, time, Redis topology, blob storage, invoice numbering, PDF lib). Your BC prompt's `<applicable_adrs>` block tells you which apply directly.
5. Your BC's chapter + glossary + example-mapping under `docs/bc-design/`
6. `docs/bc-design/events-catalog.md` + `use-cases.md` — find rows for your BC
7. `docs/bc-design/error-taxonomy.md`, `kafka-dlq-strategy.md`, `avro-compatibility.md`, `architecture-tests.md`
8. Saga-affected BCs also read: `saga-stuck-runbook.md`. BFF reads: `rate-limiting.md`. Invoicing reads: ADR-0017/0018/0019.
9. `docs/diagrams/context-map.md` + `bc-map-entities.md` (mermaid sources)

## 3. Stack conventions

.NET 10 microservices:

- **ASP.NET Core** + **FastEndpoints 7.0.1** (HTTP)
- **EF Core** + **PostgreSQL** (`Npgsql.EntityFrameworkCore.PostgreSQL`), snake_case via `EFCore.NamingConventions`
- **KafkaFlow** for Kafka consumers/producers
- **Confluent Schema Registry** + **Avro** via `Platform.Avro.UniversalSerDes`
- **FluentValidation** (validators), **FluentResults** (`Result` / `Result<T>`), **Ardalis.SmartEnum**, **Ardalis.Specification.EntityFrameworkCore**, **Riok.Mapperly**
- **Redis** (split: `redis-basket` AOF for Basket primary; `redis-cache` volatile for BFF backplane + ASP.NET Output Cache backing per ADR-0013/0016) + **FusionCache** (Basket + BFF only)
- **MassTransit** + EF Core saga persistence (saga/ only)
- **OpenTelemetry** + OTLP collector; Seq dev sink
- **Azure.Storage.Blobs** + **Azurite** (local) for blob storage (Invoicing)
- **OpenFeature** + JSON-file provider for feature flags (per ADR-0014)
- **Keycloak** OAuth2 (user auth + service-account client-credentials for service-to-service per ADR-0010)
- **xUnit** + **Testcontainers**
    - xunit.v3 enforces `xUnit1051` — pass `TestContext.Current.CancellationToken` to every async call in a test body, otherwise the build fails.
    - ASP.NET middleware tests built on `DefaultHttpContext` do **not** fire `Response.OnStarting` callbacks (the callbacks are registered but never invoked without a real pipeline). Either set response headers eagerly, or stand up `Microsoft.AspNetCore.TestHost` for the assertion.

Do not introduce new libraries without documenting rationale + asking.

## 4. Weather reference catalog — file paths to mirror

The Weather service is a complete working reference. **Copy the shape, not the domain.**

| Concept | Weather file | Purpose |
|---|---|---|
| Aggregate root | `src/Weather.Domain/Alerts/AlertSubscriber.cs` | Private ctor, static factories, `AddDomainEvent` |
| SmartEnum | `src/Weather.Domain/Alerts/ValueObjects/SubscriptionTier.cs` | Business-rule properties on values |
| Value object | `src/Weather.Domain/Alerts/ValueObjects/` | `ValueObject` base; `Create` returning `Result<T>` |
| Internal domain event | `src/Weather.Domain/Alerts/Events/` | `sealed record {Name}DomainEvent : DomainEvent` |
| Command handler + validator | `src/Weather.Application/WeatherAlerts/PurchaseSubscription/` | Command class + `{Name}CommandHandler` + `{Name}CommandValidator` |
| Query handler | `src/Weather.Application/WeatherForecast/GetForecasts/` | Query + handler + response DTO |
| Outbox publisher | `src/Weather.Application/WeatherAlerts/PurchaseSubscription/SubscriptionActivatedOutboxPublisherDomainEventHandler.cs` | Internal→external event mapping + outbox add |
| Application DI | `src/Weather.Application/Common/ApplicationDependencyInjection.cs` | Validators, CQRS handlers, domain-event dispatcher, behaviours |
| Infrastructure DI | `src/Weather.Infrastructure/Common/MessagingDependencyInjection.cs` | Outbox + inbox + KafkaFlow consumer setup |
| Persistence DI | `src/Weather.Infrastructure/Common/PersistenceDependencyInjection.cs` | DbContext + `DispatchDomainEventsInterceptor` + FusionCache |
| Kafka consumer | `src/Weather.Infrastructure/Messaging/Kafka/Subscriptions/ActivateSubscriptionCommandKafkaHandler.cs` | `IMessageHandler<T>` + inbox dedup + transactional handler |
| API endpoint | `src/Weather.Api/Endpoints/` | FastEndpoints subclasses |
| Result → Problem-Details | `src/Weather.Api/Common/Extensions/ResultsExtensions.cs` | `SendErrorResponseAsync` |
| Arch tests | `test/Weather.ArchitectureTests/` | NetArchTest assertions |
| Integration tests | `test/Weather.IntegrationTests/` | `WebApplicationFactory` + Testcontainers |
| Functional tests | `test/Weather.FunctionalTests/` | End-to-end HTTP |

## 5. Platform libraries (consume, don't modify — Wave 0 extends `ServiceDefaults`)

- `Platform.SharedKernel` — `Entity<TId>`, `AggregateRoot<TId>`, `ValueObject`, `DomainEvent`, `DataIntegrityException`, **`Money`**, **`Address`**, **`IClock`** (Wave 0 additions per ADR-0015)
- `Platform.CQRS` — `ICommand[<T>]`, `IQuery<T>`, handler + dispatcher + behaviour chain (Tracing → Logging → Metrics → Validation → Handler)
- `Platform.ReliableMessaging.Outbox.EFCore` — `ITransactionalOutbox<TDbContext>.AddOutboxMessage(topic, key, ISpecificRecord)`
- `Platform.ReliableMessaging.Inbox.EFCore` — `AddInbox<TDbContext>(typeof(...))` for Kafka dedup
- `Platform.KafkaFlow.DeadLetter` — `.DLT` suffix convention (see kafka-dlq-strategy.md)
- `Platform.KafkaFlow.Inbox.EFCore`, `Platform.KafkaFlow.ProducerHeaders` — Wave 0 extends with correlation-id producer + consumer middleware (ADR-0008)
- `Platform.Avro.UniversalSerDes` — Record-Name-Strategy subjects
- `Platform.SchemaRegistry.Contracts` — all `.avsc` files live here
- `Platform.OutboxRelay.WorkerService` — you ADD a container per service schema in docker-compose
- `Platform.ServiceDefaults` — OTel + health checks + problem details + **Wave 0 additions: correlation-id middleware, OAuth2 service-auth, OpenFeature DI, JSON `DateTimeOffset` converter, named Polly resilience presets**. BCs read the ambient correlation id via `CorrelationIdContextKeys` (public constants for the HTTP header name, `HttpContext.Items` key, OTel `Activity` tag, and Serilog property) — see `platform/Platform.ServiceDefaults/CorrelationId/CorrelationIdContextKeys.cs`.
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

## 7. Skills you invoke during the session (universal)

Every BC prompt extends this list with BC-specific skills. Invoke skills proactively at the trigger.

| Phase | Skill | Trigger |
|---|---|---|
| Session start | `superpowers:using-superpowers` | auto — establishes how you find and use skills |
| Before design | `superpowers:brainstorming` | before writing code for a new feature — explores the unspecified internal shape |
| Phased planning | `nw-roadmap` | decompose your BC into phased TDD steps aligned with the example-mapping scenarios |
| Per step | `superpowers:test-driven-development` | red → green → refactor discipline on each roadmap step |
| Step dispatch | `nw-execute` | optional — delegate one roadmap step to a specialised agent |
| When stuck | `superpowers:systematic-debugging` | unexpected behaviour, flaky test, inconsistent reproduction |
| Post-green quality | `nw-mutation-test` | feature-scoped mutation testing; target ≥ 80% kill rate |
| Refactor pass | `nw-refactor` | RPP L1–L6 after tests are green |
| Before PR | `superpowers:requesting-code-review` | structured review request |
| On feedback | `superpowers:receiving-code-review` | rigour before implementing review suggestions |
| **Pre-commit, every milestone** | `Agent(subagent_type="feature-dev:code-reviewer", model="opus")` | **mandatory** on any milestone commit touching ≥ 5 files — brief with file list + test list + what's intentionally deferred. M2 precedent: this pass caught one CRITICAL + three IMPORTANT findings that would otherwise have shipped. Use `opus` explicitly; the default model is weaker. |
| Before claiming done | `superpowers:verification-before-completion` | evidence-first claim of completion (run all `<verification>` commands; paste output in summary) |
| Final peer review | `nw-software-crafter-reviewer` | invoke with your session summary BEFORE declaring DoD met; fix any HIGH-severity findings. Runs on Haiku — complement the Opus pre-commit review, don't substitute for it. |
| .NET idioms | `dotnet-contribution:dotnet-backend-patterns` | continuous — C#/.NET pattern reference |

> **Reviewer choice note.** Some older prompts reference `code-documentation:code-reviewer` for the pre-commit review step. The concrete `Agent(subagent_type="feature-dev:code-reviewer", model="opus")` call above is the validated path (proven on Wave 0 M2) — use it unless you have specific reason to pick a different reviewer skill.

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
- Wave 0 (`docs/implementation-prompts/wave-0-platform-prep.md`) has not been merged but your BC depends on its outputs (correlation-id middleware, service-auth, redis-basket / redis-cache split, Azurite container, etc.).
- An ADR contradicts a BC design doc.
- A `<contract>` item conflicts with `events-catalog.md` or `use-cases.md` (file:line).
- An open design decision in `<design_open>` has implications you didn't expect — flag the trade-off, name your tentative choice, ask before committing.
- You're about to introduce a new platform library or NuGet package not listed in `<reading_order>` or this `_shared.md` § 3.
- You're about to skip a step in the dispatch sequence (e.g., starting Wave 2 before Wave 1 is verified).
- Your context window approaches 80% full — summarise progress and ask whether to continue or hand off.

## 10. Session management

Every BC implementation is multi-file, multi-hour work. Manage the session as you would a long PR:

- **Commit in logical milestones, not per-file.** Suggested chunks: scaffold + unit-test scaffolding (1 commit) → domain layer + unit tests (1 commit) → application layer + handlers + outbox publishers (1 commit) → infrastructure layer + DI + Kafka consumers (1 commit) → integration tests (1 commit) → architecture tests (1 commit) → docker-compose delta (1 commit) → docs self-corrections (1 commit). Tune to your BC's shape.
- **Test before committing.** After each milestone, run the relevant test slice from `<verification>`. Do not accumulate untested work — `dotnet test` failures debugged after 5 commits is much harder than after 1.
- **Surface progress.** After each milestone, summarise to the user: "Completed `<dod_item>`; tests green; moving to `<next_item>`." This lets the user catch direction problems before more work compounds.
- **Context-window discipline.** When approaching 80% full (≈ 30 large files read), stop, summarise, and ask whether to continue or hand off the remainder to a follow-up session with a context-summary.

## 11. Peer review before declaring done

**Pre-commit, every milestone** (not only at DoD):

0. Before `git commit` on any milestone that touches ≥ 5 files, invoke `Agent(subagent_type="feature-dev:code-reviewer", model="opus")`. Brief it with the exact file list, test list, design decisions taken, and what's intentionally deferred. Fix all CRITICAL/HIGH findings before staging; document accepted MEDIUM/LOW findings in the commit body. Use `model="opus"` — the Wave 0 M2 precedent showed the default Sonnet surfaced one CRITICAL + three IMPORTANT findings; Opus is strictly stronger for the same cost posture on a single review call.

Before posting your session summary as "complete":

1. Run every command in `<verification>` and paste the pass/fail output (not a summary — the actual output) into your session summary.
2. Invoke `superpowers:verification-before-completion` (per § 7) — its checklist catches the common "I claimed done but didn't actually run X" gap.
3. Invoke `nw-software-crafter-reviewer` with your session summary as input — it reviews against the BC's contract, applicable ADRs, and code quality. Fix any HIGH-severity findings before declaring done. MEDIUM/LOW findings can be documented as follow-ups in the summary. This reviewer runs on Haiku for cost; it complements but does not substitute for step 0's Opus pre-commit pass.

## 12. Shared Definition of Done

Each BC adds specifics; these are universal.

- [ ] 4-layer project (`Api`, `Application`, `Domain`, `Infrastructure`) compiles; BFF has 2 layers only (`Api`, `Infrastructure`), saga has none (only `saga/SagaOrchestrators/Checkout/`).
- [ ] All commands + queries from use-cases.md § {your BC} implemented
- [ ] All internal `*DomainEvent` types declared in Domain layer
- [ ] All external `*Event` Avro schemas created under `platform/Platform.SchemaRegistry.Contracts/Avro/{Domain}/{Aggregate}/`
- [ ] Outbox publishers map internal → external per BC chapter
- [ ] DbContext + naming conventions scaffolded (migration user-generated per CLAUDE.md)
- [ ] Messaging DI: outbox, inbox, Kafka consumers per BC
- [ ] docker-compose delta: topics + outbox-relay-{bc} container
- [ ] 4 test projects compile + pass; architecture tests enforce the rules in architecture-tests.md § {your BC}
- [ ] All HTTP routes under `/api/v1/{bc}/...` per ADR-0012
- [ ] All timestamps `DateTimeOffset` (persisted as `timestamptz`); no `DateTime.UtcNow` in domain (arch test) — per ADR-0015
- [ ] Correlation-id propagation working (HTTP → Kafka → DB column) per ADR-0008
- [ ] `dotnet build -m`, `dotnet restore --locked-mode`, `dotnet format whitespace`, `dotnet format style` all green
- [ ] `docker compose --profile full up -d` starts the container + healthcheck passes
- [ ] Docs self-corrected if needed
- [ ] Peer-review chain (§ 11) executed; HIGH-severity findings fixed
- [ ] Session summary posted

## 13. What "done" is NOT

- Uncommented code that compiles. Run tests. Start the container.
- Silent deviation from the BC chapter. Flag + justify.
- Missed external events — event catalog is contract; every listed event has a schema file AND an outbox publisher.
- Handwaved validators — every command has one OR a documented reason for skipping.
- Out-of-date docs after self-correction — if code disagrees with doc and code is right, the doc MUST be updated in the same session.
- "Tests passed locally" without paste of `dotnet test` output in the session summary.
- ADR violations — if you decide an ADR doesn't apply to your BC, document the rationale, don't silently skip.
