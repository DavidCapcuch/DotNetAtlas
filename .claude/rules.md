# Standing rules

Non-negotiable behavioral constraints for every session. Terse on purpose; the conventions + rationale they build on live in `CLAUDE.md` (auto-loaded) and `docs/bc-design/`. These add the **dispatch discipline** CLAUDE.md doesn't state — they deliberately do not restate its conventions (result pattern, Avro/migration regen all live there).

- Don't claim "done" without pasting the actual output of the **verification gates** — run them via the `daca-gates` skill, which owns the gate set and its flags; this repo's deltas are in `docs/verification-gates.md`. Evidence before assertions.
- Every new behavior ships with new tests. New behavior is not "done" on green pre-existing tests alone.
- When uncertain about a cross-BC contract, an ADR, or an architectural seam, ask — don't guess. A 60-second pause beats a silent wrong assumption.
- Never force-push. Branch before committing off `main`; commit / push only when asked.
