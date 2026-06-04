# Implementation Prompts — BC dispatch kit

> Master System Prompts for dispatching Bounded Context (and cross-cutting) implementation work into **independent Claude Code sessions**. Each prompt is self-contained and, when referenced from a fresh session, equips the agent to implement the work autonomously with the right skill calls, ADR awareness, and verification discipline.

## How the kit fits together

- **`_shared.md`** — universal operating manual. Reading order, stack conventions, Weather catalog, platform libraries, async/sync rule, universal skills, autonomous-evolution protocol, universal stop conditions, session-management rules, peer-review chain, shared DoD. Every prompt tells the agent to read this FIRST.
- **`_template.md`** — the canonical prompt structure (XML-tagged sections + `<thinking_first>` directive). Every dispatch prompt follows it. New prompts copy this template and fill the brackets.
- **`{prompt}.md`** — one prompt per dispatch unit. Lean (≤ 250 lines, including XML structure). Follows `_template.md` exactly.

The 8 prompts:

| Prompt | Wave | Depends on |
|---|---|---|
| [`wave-0-platform-prep.md`](wave-0-platform-prep.md) | 0 | — |
| [`catalog.md`](catalog.md) | 1 | Wave 0 |
| [`basket.md`](basket.md) | 1 | Wave 0 |
| [`ordering.md`](ordering.md) | 1 | Wave 0 |
| [`inventory.md`](inventory.md) | 1 | Wave 0, Catalog (event consumer) |
| [`payments.md`](payments.md) | 1 | Wave 0 |
| [`invoicing.md`](invoicing.md) | 1 | Wave 0 (consumes Ordering + Payments events but can scaffold independently) |
| [`checkout-saga.md`](checkout-saga.md) | 2 | Wave 1 BCs |
| [`bff.md`](bff.md) | 3 | Wave 1 + Wave 2 |

## Dispatching a session

Create a new Claude Code session in the repo. Paste this template as the first user message:

````markdown
Implement the **{BC}** bounded context per `docs/implementation-prompts/{bc}.md`.

Session notes (optional — add delta context for this run):
- <e.g. "Focus on search endpoint first; rest in next session">
- <e.g. "I've already scaffolded Catalog.Domain; pick up from there">
- <e.g. "Apply PR #42's caching suggestion on search endpoint">
- <e.g. "Skip FunctionalTests this session — Wave 3 dependency">

Follow the prompt's <thinking_first> directive — your first response is the plan, not code.
````

The stored MD is the **reusable contract**; the session notes are **per-dispatch delta**. Version-controlled stability + session-specific tailoring.

## Dispatch order

```
Wave 0 (alone):       wave-0-platform-prep
Wave 1 (parallel):    Catalog ∥ Basket ∥ Ordering ∥ Inventory ∥ Payments ∥ Invoicing
Wave 2 (depends 1):   Checkout saga
Wave 3 (depends 1+2): BFF
```

Wave 0 must merge before Wave 1 dispatches. Within Wave 1, the six BCs are independent at the contract level; Inventory consumes `ProductCreatedEvent` from Catalog so wire-up testing benefits from Catalog landing first, but they can scaffold + unit-test in parallel.

Ordering is greenfield under `services/Ordering/` (the former `services/Order/` was deleted pre-dispatch with the Weather cleanup).

## Evaluating a completed prompt

When an agent reports completion, the agent MUST have already done the peer-review chain (`_shared.md § 11`). Re-verify:

1. `dotnet build -m` → succeeds
2. `dotnet restore --locked-mode` → succeeds with committed lock files
3. `dotnet format whitespace --no-restore --verify-no-changes` → clean
4. `dotnet format style --no-restore --verify-no-changes` → clean
5. `dotnet test test/{Bc}.*.Tests/` → all green
6. `docker compose --profile full up -d` → service container starts + healthcheck passes
7. Docs self-corrected if needed (`docs/bc-design/{bc}.md`, glossary, example-mapping)
8. Session-summary posted with the full template from `_template.md § session_summary` — including ADR application notes, verification output (not a summary — the actual output), and peer-review findings

## Contract-locked vs Design-open — the core idea

Each prompt explicitly splits:

- **`<contract>`** — the seams between BCs (events, topics, Avro, cross-BC HTTP calls). These are inviolate; changing any requires user approval + doc update + ADR if material.
- **`<design_open>`** — everything inside the BC (code shape, specification composition, validator mechanics, error-class API, test-split depth, tooling choices). These are the agent's own.

This is the spec-driven-at-the-seams + discovery-driven-within-the-BC model. Agents are expected to invoke `superpowers:brainstorming` before designing any open area and `nw-roadmap` before starting to write code.

## Cross-cutting ADRs (0008–0019) — the `<applicable_adrs>` block

Each BC prompt has an `<applicable_adrs>` block listing the cross-cutting ADRs that apply to that BC. The agent reads each ADR once and refers back when implementing the relevant code path. Common applications:

- **ADR-0010 Service-to-Service Auth** — every BC that publishes commands or makes outbound HTTP
- **ADR-0011 PII Handling** — Ordering, Payments, Invoicing, BFF (where addresses / payment tokens / buyer data flows)
- **ADR-0012 API Versioning** — every BC with HTTP endpoints
- **ADR-0013 Idempotency-Key (FastEndpoints)** — BFF checkout, Basket items, Ordering cancel, Invoicing resend
- **ADR-0014 Feature Flags** — Catalog (`catalog.show-discontinued-in-search`), BFF (`bff.home-page-eager-cache-warm`), Checkout saga (`checkout.payment-then-stock`)
- **ADR-0015 Time / Timezone** — every BC with timestamps (all of them)
- **ADR-0016 Redis Topology** — Basket (uses `redis-basket`), BFF + ASP.NET Output Cache (use `redis-cache`)
- **ADR-0017 Blob Storage (Azurite)** — Invoicing (PDFs)
- **ADR-0018 Invoice Numbering** — Invoicing only
- **ADR-0019 PDF Generation (QuestPDF)** — Invoicing only

## Skill integration

Every prompt has a `<skills>` block. Universal skills (in `_shared.md § 7`):

- `superpowers:using-superpowers`, `brainstorming`, `test-driven-development`, `systematic-debugging`, `verification-before-completion`, `requesting-code-review`, `receiving-code-review`
- `nw-roadmap`, `nw-execute`, `nw-mutation-test`, `nw-refactor`
- `nw-software-crafter-reviewer` — final peer review before declaring done
- `dotnet-contribution:dotnet-backend-patterns`

BC-specific extras are in each prompt's `<skills>` block.

## Self-correction expectations

Every prompt's `<autonomous_evolution>` tells the agent to:

1. Flag doc inconsistencies (file:line)
2. Propose improvements (don't implement without approval)
3. Self-correct the domain model when integration tests or example mapping surface a missing rule — update `{bc}.md` + `example-mapping/{bc}.md` BEFORE implementation
4. Ask on non-obvious tradeoffs rather than silently guessing

The session summary is the evidence trail — every dispatch ends with one (template in `_template.md § session_summary`).
