# Documentation conventions

The **doc-style SSOT** — what good documentation looks like in this repo. The build agent follows it; `daca-documentation-reviewer` audits the diff against it. (Code style — `var`, expressive names, AAA, test naming — is `conventions.md § 9`.)

## Principle

Docs exist only to help a future engineer **decide, understand, operate, or safely change** the system. Litmus: delete the code → the docs still add value; delete the docs → no non-obvious knowledge is lost.

**Audience** — write for an engineer who is sharp but has never seen this codebase: spell out the terms, context, and non-obvious decisions they'd lack; assume the general competence they'd have.

## XML doc-comments

- **Intent, not boilerplate** — a `<summary>` on public domain API explains the *why* / invariants; it never restates the member name (`CS1591` is silenced, so a doc is a deliberate choice — a name-restating doc is worse than none).
- **Aggregates document the events they raise** — a class-level list ("can raise the following domain events…") + per public method a "Raises `X` when `Y`" note.

## Comments

- **Why, not what** — explain intent / invariants / non-obvious constraints **and the assumptions + trade-offs behind a decision**; never narrate what self-documenting code already says.

## Markdown, runbooks & knowledge transfer

- **High signal-to-noise** — scannable (bullets / tables / short paragraphs); no unbroken prose block over ~4 sentences.
- **Invariant / boundary changes documented** — a new or altered domain invariant, state-transition rule, or architectural boundary is recorded in the primary design doc / README in precise ubiquitous language.
- **Interface changes** → API contracts + examples updated.
- **Actionable runbooks** — a new operational procedure / config key / feature flag ships copy-pasteable steps + expected outcome; no theory or history.
- **No AI-meta commentary** — zero session narratives ("in this session we…"), polite intros/outros, or chronological coding-log artifacts in code or markdown.
- **No migration backstory** — document the *current* state, not how it got there. "is now X", "previously Y, now Z", "this used to…" rot the moment the next change lands; the transition belongs in version control, not the prose.
- **One SSOT per enumerated set; state the rule, link the values** — when a set of values has a designated source-of-truth doc (e.g. the [service-scope matrix](../../src/keycloak/service-scope-matrix.md) owns the client↔scope↔audience enumeration), other docs (ADRs, READMEs, compose comments) state the governing *rule* and link the SSOT — they never re-list the set, which drifts across copies on the next change (the audience mappings drifted apart across ADR / README / compose before they were reconciled).
