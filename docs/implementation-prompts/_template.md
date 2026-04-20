# Master System Prompt — Implement the **{BC Name}** Bounded Context

> **This is the canonical template.** Every BC dispatch prompt under `docs/implementation-prompts/` follows this structure.
>
> Replace `{BC Name}` and the bracketed sections. Keep the XML tags and the `<thinking_first>` block at top — they drive the agent's first response. Mark drift from this template in your prompt's session summary.
>
> Paste the prompt as the first message in a fresh Claude Code session for `C:\Users\dcapc\Desktop\Git\DotNetAtlas`.

<thinking_first>
Before writing any code, do these in your **first response** — explicitly, in order:

1. **Read every file under `<reading_order>`** in order. State your understanding of what's locked vs open.
2. **Verify prerequisites.** List anything in `<prerequisites>` that isn't satisfied. STOP and ask if any.
3. **Surface contradictions.** If a doc-pair disagrees, paste both `file:line` references and ask. Silent guessing is worse than a 60-second pause.
4. **Confirm applicable ADRs.** For each ADR in `<applicable_adrs>`, name what it implies for THIS BC's code (one line each).
5. **State your plan.** Group your DoD items into commit milestones (typically 5–8 commits, not per-file). Confirm with the user before starting code.
6. **Acknowledge stop conditions.** Recite the `<stop_conditions>` from this prompt and from `_shared.md § 9` so they're in working memory.
</thinking_first>

<mission>
{One paragraph: what you implement, the deliverable, when "done" looks like}
</mission>

<prerequisites>
- Wave 0 platform prep merged (`docs/implementation-prompts/wave-0-platform-prep.md`)
- {Any other BCs that must precede this one — usually empty for Wave 1, populated for Wave 2/3}
</prerequisites>

<role_in_system>
{What role this BC/component plays. Upstream / downstream. Teaching purpose.}
</role_in_system>

<contract>
LOCKED at the seams. Do not change any of these without raising the issue first (see `<stop_conditions>`).

- {Locked event surfaces, topics, command schemas with refs to `events-catalog.md § X`}
- {Locked HTTP route shapes with refs to `use-cases.md § X`}
- {Avro compatibility modes per ADR-0007}
- {Topic retention policies}
- {Cross-BC consumer groups (if BC produces events others consume)}
- {File ownership — see `<boundaries>`}
</contract>

<design_open>
You own these. Justify each in your session summary.

- {Aggregate backing-field shape, EF mapping choices}
- {Specification classes for queries}
- {Validator mechanics}
- {`{BC}Errors` factory shapes (names locked by `error-taxonomy.md`)}
- {Test-split depth}
- {Architecture-test tooling}
- {Additional `example-mapping` sessions when integration tests surface gaps}
</design_open>

<reading_order>
1. `docs/implementation-prompts/_shared.md` — FIRST
2. `docs/bc-design/{bc}.md` — full BC spec
3. `docs/bc-design/glossary-{bc}.md` + `example-mapping/{bc}.md`
4. `docs/bc-design/events-catalog.md` § {your BC's section}
5. `docs/bc-design/use-cases.md` § {your BC's section}
6. `docs/bc-design/error-taxonomy.md` § {your BC's section}
7. {Other relevant cross-cutting docs — ADRs, runbooks, rate-limiting}
8. **All cross-cutting ADRs in `<applicable_adrs>`** — read each once, refer back when implementing the relevant code
9. {Existing Weather references the BC mirrors per `_shared.md § 4`}
</reading_order>

<applicable_adrs>
Cross-cutting decisions you must apply. Read once; refer back when implementing the relevant code path.

- [ADR-XXXX](../adr/XXXX-slug.md) — {one-line on what it implies for THIS BC}
- {Repeat for each ADR that this BC must apply}
</applicable_adrs>

<skills>
Universal skills per `_shared.md § 7`. {BC} -specific additions:

| Phase | Skill | When |
|---|---|---|
| {When} | `{plugin:skill}` | {Trigger} |
</skills>

<autonomous_evolution>
{BC} -specific triggers (extending `_shared.md § 8`):

- {Specific decision the agent must propose-and-justify or stop-and-ask}
- {Specific integration concern (e.g., consumer-group naming collision)}
- {Edge case likely to surface during integration tests that should be documented as a new example-mapping session before implementing}
</autonomous_evolution>

<success_criteria>
Semantic outcomes (different from `<dod>` checklist below):

- {What downstream BCs/agents can do as a result of this BC being complete}
- {What invariants hold at the BC boundary}
- {What teaching pattern is now visibly demonstrated}
</success_criteria>

<dod>
Concrete deliverables. Extends `_shared.md § 12`.

- [ ] {Specific deliverable for this BC, observable as a test or compose state}
- [ ] {Repeat per BC-specific outcome}
- [ ] All `<applicable_adrs>` enforced (architecture tests + verification commands)
- [ ] Peer-review chain (`_shared.md § 11`) executed; HIGH findings fixed
</dod>

<boundaries>
**You may write:**
- `services/{BC}/**`
- `test/{BC}.*.Tests/**`
- `platform/Platform.SchemaRegistry.Contracts/Avro/{BC}/**`
- `docker-compose.yaml` (your topics + `outbox-relay-{bc}` only)
- `Directory.Packages.props` (your packages only)
- `docs/bc-design/{bc}.md` + glossary + example-mapping (self-correction only — record it in the session summary)

**Do not touch:**
- Other BCs' services
- Other BCs' Avro schemas
- Saga orchestrators (Wave 2 owns them)
- Platform libraries (only your `.avsc` files)
- Weather business code
- EF Core migrations (per CLAUDE.md — user-generated only)
</boundaries>

<stop_conditions>
STOP and ask the user (in addition to `_shared.md § 9` universal stops) if:

- {BC-specific contract conflict surfaces}
- {Specific dependency that, if missing, blocks core work}
- {Decision that would propagate side effects to other BCs}
</stop_conditions>

<session_management>
Per `_shared.md § 10`. {BC}-specific suggested commit milestones:

1. Scaffold 4 layers + project references; `dotnet build` green
2. Domain layer + unit tests
3. Application layer + handlers + outbox publishers; outbox roundtrip integration test
4. Infrastructure layer + DI + Kafka consumers
5. Architecture tests
6. {BC}-specific integration tests (per `example-mapping`)
7. docker-compose delta + verification
8. Docs self-corrections + session summary

Adjust to your BC's shape.
</session_management>

<verification>
```bash
dotnet build -m
dotnet restore --locked-mode
dotnet format whitespace --no-restore --verify-no-changes
dotnet format style --no-restore --verify-no-changes
dotnet test test/{BC}.UnitTests/
dotnet test test/{BC}.ArchitectureTests/
dotnet test test/{BC}.IntegrationTests/
dotnet test test/{BC}.FunctionalTests/
docker compose --profile full up -d
# {BC}-specific smoke checks (see <dod>)
```

Paste actual command output (pass/fail per command) into your session summary, not a summary.
</verification>

<example_design_decision>
For one of the `<design_open>` items, here's the depth expected in your session summary:

**Question:** {pick a representative open design decision}

**Bad answer:** {one-line, no rationale, picks the obvious option without trade-off thinking}

**Good answer:** {3–5 sentences, names the chosen option, lists 2–3 reasons, names the trade-off accepted, and points to the test/code that verifies the choice works}

That's the depth expected for **every** `<design_open>` resolution.
</example_design_decision>

<peer_review>
Per `_shared.md § 11`. Before declaring DoD met:

1. Run every command in `<verification>`; paste pass/fail output in session summary.
2. Invoke `superpowers:verification-before-completion`.
3. Invoke `nw-software-crafter-reviewer` with your session summary as input — fix any HIGH-severity findings.

Do NOT mark `<dod>` complete until all three are done.
</peer_review>

<session_summary>
Post at the end of the session:

```
## {BC} BC — Session Summary

### Files created
- code: <n>
- tests: <n>
- Avro schemas: <n>
- docker-compose delta: <n>
- doc updates: <n>

### Decisions (rationale per <design_open> item)
- {Decision 1}: {chosen option} — {2-3 reasons} — {trade-off accepted}
- ...

### ADR application notes
- ADR-XXXX: {how it was applied + which test verifies}
- ...

### Inconsistencies found (file:line → description)
- ...

### Improvements proposed (NOT implemented unless approved)
- ...

### Domain self-corrections
- {updates to bc-design / glossary / example-mapping with rationale}

### Verification output
- `dotnet build`: <output>
- `dotnet test`: <output>
- ...

### Peer-review findings (`nw-software-crafter-reviewer`)
- HIGH (fixed): ...
- MEDIUM (deferred): ...
- LOW (deferred): ...

### Open questions
- ...
```

Proceed.
</session_summary>
