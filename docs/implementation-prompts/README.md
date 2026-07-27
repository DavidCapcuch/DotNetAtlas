# Implementation Prompts — BC dispatch kit

> Master System Prompts for dispatching Bounded Context (and cross-cutting) implementation work into **independent Claude Code sessions**. Each prompt is self-contained and, when referenced from a fresh session, equips the agent to implement the work autonomously with the right skill calls, ADR awareness, and verification discipline.

## How the kit fits together

- **`_shared.md`** — universal operating manual. Reading order, stack conventions, **golden-reference model** (§ 4), platform libraries, async/sync rule, **the dispatch lifecycle + its skills** (§ 7), autonomous-evolution protocol, stop conditions, session-management, **the three-role review stack** (§ 11), shared DoD. Every prompt tells the agent to read this FIRST.
- **`_template.md`** — the canonical prompt structure (XML-tagged sections + `<thinking_first>` directive). Every dispatch prompt follows it. New prompts copy this template and fill the brackets.
- **`bff.md`** — the one live dispatch prompt (the unbuilt **BFF**, Wave 3) and the worked exemplar of the current template.

The Wave 0–2 per-BC prompts (catalog / basket / ordering / inventory / payments / invoicing / checkout-saga) were **retired after their BCs shipped** — they had rotted into history. To dispatch a *new* BC, copy `_template.md` and fill it from the BC's `bc-design` chapter; `bff.md` is the model.

## The dispatch lifecycle

A dispatch is a phased pipeline, not just a build session. The authoritative phase→skill→trigger table is `_shared.md § 7`; the diagram below is the quick view:

```
0. sharpen design   grill-with-docs                ← stress-test the bc-design chapter vs glossary + ADRs
1. decompose        to-spec → to-tickets           ← tracer-bullet vertical slices, each demoable
2. dispatch         _template.md / bff.md          ← the locked-contract spec the build session runs
3. build loop       tdd                            ← red → green → refactor, per behaviour
4. gate             verification-before-completion ← the four hard gates, actual output pasted
5. DoD gate         daca-dod-reviewer (+ delegates)     ← diff vs its DoD bar; arch/DDD → daca-bc-consistency-reviewer, docs → daca-documentation-reviewer
```

Phases 0–1 happen with the owner before a fresh session is spawned; phases 2–5 run inside the dispatch. This mirrors [Anthropic's harness-design guidance](https://www.anthropic.com/engineering/harness-design-long-running-apps): agree the scope + verification contract *before* implementation, and keep the agent that **builds** separate from the agent that **judges**.

## Dispatching a session

Create a new Claude Code session in the repo. Paste this template as the first user message:

````markdown
Implement the **{BC}** bounded context per `docs/implementation-prompts/{bc}.md`.

Session notes (optional — add delta context for this run):
- <e.g. "Focus on search endpoint first; rest in next session">
- <e.g. "I've already scaffolded Catalog.Domain; pick up from there">
- <e.g. "Apply PR #42's caching suggestion on search endpoint">
- <e.g. "Skip IntegrationTests this session — Wave 3 dependency">

Follow the prompt's <thinking_first> directive — your first response is the plan, not code.
````

The stored MD is the **reusable contract**; the session notes are **per-dispatch delta**. Version-controlled stability + session-specific tailoring.

## What's left to dispatch

Waves 0–2 are **built** (platform + the six Wave-1 BCs + the checkout saga). The only remaining dispatch is **Wave 3 — the BFF** (`bff.md`), which depends on the Wave 1 BCs (HTTP-reachable) + the Wave 2 saga. The wave model is retained as the **file-ownership discipline** that kept parallel sessions conflict-free, not as a live schedule.

## Evaluating a completed prompt

When an agent reports completion, the agent MUST have already run the three-role review stack (`_shared.md § 11`): Opus pre-commit review, the gates with pasted output, and `daca-dod-reviewer` (which delegates drift → `daca-bc-consistency-reviewer` and docs → `daca-documentation-reviewer`). Re-verify:

1. All `daca-gates` gates green (build / restore `--locked-mode` / format / the three test projects / compose health) — actual output, not a summary.
2. Docs self-corrected if needed (`docs/bc-design/{bc}.md`, glossary, example-mapping).
3. `daca-dod-reviewer` blockers fixed; its Self-attested bucket attested.
4. Session-summary posted with the full template from `_template.md § session_summary` — ADR notes, pasted verification output, and review-stack findings.

## Contract-locked vs Design-open — the core idea

Each prompt explicitly splits:

- **`<contract>`** — the seams between BCs (events, topics, Avro, cross-BC HTTP calls). These are inviolate; changing any requires user approval + doc update + ADR if material.
- **`<design_open>`** — everything inside the BC (code shape, specification composition, validator mechanics, error-class API, test-split depth, tooling choices). These are the agent's own.

This is the spec-driven-at-the-seams + discovery-driven-within-the-BC model. The open interior is grounded by `grill-with-docs` (phase 0) and built behaviour-by-behaviour with `tdd` (phase 3) — see the lifecycle above and `_shared.md § 7`.

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

The authoritative phase→skill→trigger mapping is the build lifecycle (`_shared.md § 7`) + the review stack (`_shared.md § 11`) — not restated here. Two notes that aren't in those tables: `superpowers:using-superpowers` auto-establishes skill usage at session start, and `requesting`/`receiving-code-review` are craft guidance, not gates. Each prompt's `<skills>` block adds unit-specific extras.

## Self-correction expectations

Every prompt's `<autonomous_evolution>` tells the agent to:

1. Flag doc inconsistencies (file:line)
2. Propose improvements (don't implement without approval)
3. Self-correct the domain model when integration tests or example mapping surface a missing rule — update `{bc}.md` + `example-mapping/{bc}.md` BEFORE implementation
4. Ask on non-obvious tradeoffs rather than silently guessing

The session summary is the evidence trail — every dispatch ends with one (template in `_template.md § session_summary`).

## Failure modes → guardrails

The kit is built to defeat the common agent-dispatch failure modes. Each maps to a concrete mechanism:

| Failure mode | Guardrail in this kit |
|---|---|
| **Context drift** (long session contradicts itself) | Context-window discipline + handoff at ~80% (`_shared.md § 9–10`); one unit per dispatch |
| **Over-editing** (touches unmentioned things) | `<boundaries>` file ownership + the migration `permissions.deny` (`.claude/settings.json`) |
| **Vague scoping** | Locked `<contract>` + `<mission>`/`<dod>`; phase-1 `to-tickets` tracer-bullet slices |
| **Missing test coverage** | "Every new behaviour ships a new test" (`_shared.md § 12`); `tdd` build loop |
| **Architecture drift** | `conventions.md` + CI-blocking `architecture-tests.md` (NetArchTest); `daca-bc-consistency-reviewer` (via `daca-dod-reviewer`) at DoD |
| **Stale docs** | Doc self-correction *in the same session* (`_shared.md § 8`) |
| **False certainty** (confident-but-wrong) | The gate's *prove-don't-claim* rule (actual pasted output) + a separate **judge** (`daca-dod-reviewer`, a fresh subagent) auditing the diff vs its DoD bar, never self-attestation |

## Basis (cited)

The kit's discipline is evidence-backed, not priors:

- **Plan-first, locked-contract-at-the-seams, separate builder from judge, hard verification thresholds** — [Anthropic, *Harness design for long-running application development*](https://www.anthropic.com/engineering/harness-design-long-running-apps).
- **Fan-out review + evaluator-optimizer** (the `daca-bc-consistency-reviewer`'s per-dimension fan-out with adversarial verification of findings) — [Anthropic, *Building Effective Agents*](https://www.anthropic.com/research/building-effective-agents).
- **Architecture-as-tests / fitness functions** make conventions executable rather than tribal — [ArchUnit](https://www.archunit.org/) (the NetArchTest model); project-specific [Roslyn analyzers](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/tutorials/how-to-write-csharp-analyzer-code-fix) enforce house style at compile time.
- **Aggregate = transactional consistency boundary, reference-by-identity** — Vaughn Vernon, *Implementing DDD* (ch. 10). **Outbox-only event seams** — [Chris Richardson, Transactional Outbox](https://microservices.io/patterns/data/transactional-outbox.html).
