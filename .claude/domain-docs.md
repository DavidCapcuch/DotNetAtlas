# Domain docs

How to consume this repo's domain documentation when exploring the codebase. Multi-context repo:
bounded contexts live one folder per context under `services/`.

## Before exploring, read these

- **`CONTEXT.md`** at the repo root, or **`CONTEXT-MAP.md`** if it exists — it points at one
  `CONTEXT.md` per context. Read each one relevant to the topic.
- **`docs/adr/`** — the ADRs touching the area you're about to work in. Also check
  `services/<context>/docs/adr/` for context-scoped decisions.
- **`docs/bc-design/`** — per-BC design docs and glossaries, plus `conventions.md`, the
  cross-cutting conventions index whose § 8 maps each topic to its canonical doc.

`CONTEXT.md` / `CONTEXT-MAP.md` are created lazily by `/grill-with-docs`, so **proceed silently
when one is missing.** Don't flag their absence or suggest creating them upfront.

## Use the glossary's vocabulary

When your output names a domain concept — an issue title, a refactor proposal, a hypothesis, a test
name — use the term as defined in `CONTEXT.md`. Don't drift to synonyms the glossary explicitly
avoids. If the concept isn't in the glossary yet, that's a signal: either you're inventing language
the project doesn't use (reconsider), or there's a real gap (note it for `/grill-with-docs`).

## Flag ADR conflicts

If your output contradicts an existing ADR, surface it explicitly rather than silently overriding:

> _Contradicts ADR-0007 (event-sourced orders) — but worth reopening because…_
