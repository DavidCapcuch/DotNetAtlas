# AI Definition of Done

The general, repo-wide "done" bar for any AI-assisted change. It complements — does not replace — the dispatch-structural DoD (`docs/implementation-prompts/_shared.md § 12`). The executable gates keep their own SSOT ([`verification-gates.md`](verification-gates.md)) and are folded in here as the first **Self-attested** item — green output pasted is non-negotiable.

**Two buckets, by who can verify each item:**

- **Self-attested** — process / conversational items that only the acting (main) session can confirm; it attests them in its session summary. An independent reviewer runs as a fresh subagent and **cannot see the conversation**, so these are not its job.
- **Reviewer-audited** — artifact-visible items the **`daca-dod-reviewer`** audits against the **diff**. It is **applicability-gated** (only items the change *triggers* are checked; the rest are reported `N/A`), **diff-scoped** (every finding cites a changed file), and severity-calibrated: an **objective violation blocks**, a **judgment concern warns**. Architecture & DDD items **delegate to `daca-bc-consistency-reviewer`** for golden-BC depth.

---

## Self-attested (main session — confirm in the session summary)

- [ ] **Verification gates green** — every gate in [`verification-gates.md`](verification-gates.md) passed, with the **actual output pasted** in the summary (the Role 2 gate; a reviewer subagent can't run them, so only the main session can attest).
- [ ] All assumptions and ambiguous / newly-surfaced requirements were **clarified with the user**, not guessed.
- [ ] The final summary **lists the underlying implementation assumptions** that were made.
- [ ] Input / boundary / state **edge cases were considered** (their tests live under *Testing*, below).
- [ ] **Post-green refactor pass ran** — after each slice went green, a dedicated refactor pass (RPP L1–L6, the `daca-dotnet-refactoring` skill) ran under green tests — never amid red; what it changed (or `fast-path: <30 LOC`) is noted in the summary.
- [ ] **Unknown-unknowns probed** — ran the `daca-blind-spots-pre-mortem` divergent pass over the change (didn't just self-attest it); its findings + how each was dispositioned are in the summary. Hidden side-effects, implicit dependencies, and misread intent surface here, not in the rubric.
- [ ] **Existing codebase patterns were evaluated first**, before introducing any novel abstraction.
- [ ] **Operational impact considered** — whether a feature flag is needed to isolate blast radius, and whether monitoring rules / alert thresholds / dashboards need updating for this change.
- [ ] **Nothing rides on manual verification** — any behaviour checked by hand this session (a poked endpoint, an eyeballed log line, a manual compose check) now has a test or gate that owns it, or the gap is explicitly surfaced in the summary. What was verified only by attention is unprotected the moment the session ends.

---

## Reviewer-audited (the `daca-dod-reviewer` checks the diff)

### Change Hygiene
- [ ] **No silent refactoring** — changes are confined to the files the task requires; zero stylistic edits to out-of-scope files.
- [ ] **No placeholders** — zero `TODO`, `throw new NotImplementedException()`, or truncated/stubbed blocks on a completed path.
- [ ] **No destructive shortcuts** — existing valid tests and business rules are retained and fixed, never deleted to make a build pass.
- [ ] Implementation logic and structure are **legible to a human reviewer** without needing a walkthrough.

### Architecture & Design  *(delegates to `daca-bc-consistency-reviewer`)*
- [ ] Matches the structural mechanics of adjacent files / the golden reference.
- [ ] No speculative abstractions, extra frameworks, or complexity beyond the requirement (YAGNI); simplest path that satisfies it.
- [ ] Any new dependency is explicitly justified and approved.
- [ ] Stays within the change's domain boundary and ownership lines.

### DDD  *(delegates to `daca-bc-consistency-reviewer`)*
- [ ] **Ubiquitous language** — class / method / variable names match the established terminology.
- [ ] **Value objects** model identity-less concepts that need self-contained validation/behaviour; avoids primitive obsession without wrapping trivial data.
- [ ] **Rich domain logic** — invariants and business rules live in entities / value objects, not leaked into anemic application services.
- [ ] **Invariant enforcement** — aggregates and value objects self-validate on construction / mutation.

### Event-Driven & Messaging Contracts
- [ ] External events that need **guaranteed delivery** are published via the transactional outbox; direct produce only for best-effort signals where loss is explicitly acceptable.
- [ ] Consumers are **idempotent** — naturally, *or* via inbox dedup where the handler has non-idempotent side-effects (don't add an inbox where the logic is already idempotent); they tolerate **at-least-once** redelivery.
- [ ] Saga steps the change touches have correct **compensation** and **timeouts** for steps that can hang, and failures map to the right outcome events.
- [ ] Topic and consumer-group names follow the conventions.
- [ ] **Observable behaviour is the contract (Hyrum's Law)** — a change to a published surface (event schema, BFF/API response) is judged by what a consumer can observe, not by what the contract documents: field semantics, enum meaning, population timing, ordering, nullability-in-practice. Schema-compatible is not consumer-compatible; anything observable that changed is called out and reviewed as a contract change.

### Data, Persistence & Concurrency
- [ ] The aggregate write and its outbox row commit in the **same transaction** (no dual-write).
- [ ] Migrations are **EF-generated**, **data-preserving** (`RenameColumn`, not `Drop`+`Add`), and ship a working `Down`. A schema change that an old running version couldn't tolerate goes **expand → contract**, not in-place.
- [ ] Timestamps are `DateTimeOffset` / `timestamptz` via `TimeProvider` — never `DateTime.UtcNow`.

### Security & Validation
- [ ] All external inputs are validated **at the boundary tier** before domain execution.
- [ ] Explicit **authentication and authorization** on every new endpoint or operation.
- [ ] **PII, keys, and secrets** are never logged, exposed in spans, or transmitted insecurely.
- [ ] The change introduces no injection, unsafe deserialization, or authorization bypass on the **new surface** it adds.
- [ ] Routes are versioned under `/api/v1` (ADR-0012); mutating endpoints carry an `Idempotency-Key` (ADR-0013).

### Testing  *(delegates to `daca-dotnet-testing-reviewer`; test-quality bar: the `daca-dotnet-effective-testing` skill)*
- [ ] Every new or changed slice has a slice test through its public entrance (HTTP / message), asserting the response plus persisted state — and the outbox row where events publish.
- [ ] Real domain logic is extracted into the domain and unit-tested exhaustively; no mocked unit tests on thin handlers.
- [ ] Tests land in the taxonomy — `{Bc}.UnitTests` / `{Bc}.IntegrationTests` / `{Bc}.ArchitectureTests` — using the BC's shared collection fixture with state reset between tests; no per-test containers or hosts.
- [ ] Explicit assertions for happy and edge/failure paths; a rejection asserts both halves (status code + nothing persisted).
- [ ] Tests are deterministic — injected `TimeProvider`, no sleeps; poll-with-deadline only where a real broker sits between act and outcome.
- [ ] Tests follow the project conventions (AAA, `Method_Scenario_ExpectedResult`, FluentAssertions — `conventions.md § 9`).

### Code Quality  *(delegates to `daca-dotnet-refactoring-reviewer`; smell taxonomy + ranking SSOT: the `daca-dotnet-refactoring` skill)*
- [ ] **Self-documenting** — names and control flow convey intent; comments justify *why*, not *what*.
- [ ] **No structural anti-patterns** — no god class / SRP break, deep nesting, or over-long methods.
- [ ] **No unaddressed design smells** — the diff is clean against the ranked Fowler/RPP smell taxonomy (`daca-dotnet-refactoring`), audited by `daca-dotnet-refactoring-reviewer`: top-ranked first (duplicated code, long method / large class, primitive obsession, divergent change / shotgun surgery, mysterious name), then feature envy, data clumps, repeated switches, mutable/global data, speculative generality, message chains, middle man. Objective smells (dead code, clone duplication, commented-out blocks, how-comments) are BLOCKERs; the rest are labelled judgment calls (WARNING), and a documented repo convention overrides them.
- [ ] Matches the project's naming conventions and directory hierarchy.
- [ ] Eliminates duplicate logic; extracts shared expressions.
- [ ] Removes dead code, commented-out blocks, and unused imports.

### Error Handling
*(The global handler + problem-details middleware are givens — these target what the feature itself adds.)*
- [ ] The feature's **expected** error cases return a typed `Result.Fail(<TypedError>)`; it throws only for bug-class violations (`DataIntegrityException` / a BC `*Exception`), never raw `ArgumentException` / `InvalidOperationException`.
- [ ] New exceptions / errors carry **actionable context** (error code + the identifiers needed to diagnose).
- [ ] The feature's user-facing error messages don't leak internal mechanics.

### Observability
- [ ] Structured logging with **named placeholders** for reliable property indexing and searchability.
- [ ] Business and operational **metrics** for critical domain state changes.
- [ ] The new code emits **spans / attributes** for its key operations (trace propagation across messaging is platform-handled).

### Performance
- [ ] No N+1 or unindexed hot-path queries.
- [ ] Result sets are bounded / paginated; no unbounded loops over external data.
- [ ] `IDisposable`s are disposed; no obvious resource leaks.
- [ ] Data and memory operations scale with data volume.

### Reliability
- [ ] **Idempotency** — duplicate messages, retried events, or identical API requests cause no corrupt state or unintended side-effects. (Three distinct layers, don't conflate: HTTP `Idempotency-Key` on mutating endpoints [Security], consumer/inbox dedup [Event-Driven], and the business-effect idempotency checked here.)
- [ ] **Graceful degradation** — partial failures or a non-critical dependency outage (downstream service, cache, external resource) are handled cleanly.
- [ ] Outbound cross-service calls use the resilience layer — **YARP at the edge**; per-service Polly only where the component owns the client (e.g. BFF).

### Documentation & high-signal knowledge transfer  *(delegates to `daca-documentation-reviewer`; litmus + style SSOT: [`documentation-conventions.md`](bc-design/documentation-conventions.md))*

- [ ] **High signal-to-noise** — markdown is scannable (bullets / tables / short paragraphs); no unbroken prose block over ~4 sentences.
- [ ] **Why, not what** — docs/comments explain intent / invariants / non-obvious constraints **and the assumptions + trade-offs behind a decision**; they never narrate what self-documenting code already says.
- [ ] **No redundant XML summaries** — no `<summary>` that merely restates a member name; aggregates document the events they raise (`documentation-conventions.md`).
- [ ] **Invariant / boundary changes documented** — a new or altered domain invariant, state-transition rule, or architectural boundary is recorded in the primary design doc / README in precise ubiquitous language.
- [ ] **Interface changes documented** — API contracts + examples are updated when an interface changed.
- [ ] **Actionable runbooks** — a new operational procedure / config key / feature flag ships copy-pasteable steps + expected outcome; no theory or history.
- [ ] **No AI-meta commentary** — zero session narratives ("in this session we…"), polite intros/outros, or chronological coding-log artifacts in code or markdown.

### Deployment & Operations
*(Feature-flag / monitoring / alert-threshold judgment is self-attested above — these are the artifact-checkable items.)*
- [ ] **Documents** new environment variables / app settings / config keys — and **never logs their values** (secrets).
