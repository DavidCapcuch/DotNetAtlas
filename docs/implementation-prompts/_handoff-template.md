# Session handoff — milestone-scoped BC work

At the end of every milestone (after the commit lands and before ending the
session), the agent emits the block below with `{BC}` and `{N+1}` substituted
for the next milestone. The user pastes it as the first message of a fresh
session.

---

Execute milestone **M{N}** of the **{BC}** bounded context per
`docs/implementation-prompts/{BC}.md` and `docs/implementation-prompts/_shared.md`.
Keep the four gates green per `_shared.md § 12`. Invoke
`feature-dev:code-reviewer` (model=opus) pre-commit per `_shared.md § 11`.
Stop-ask if you'd touch anything outside M{N}'s boundary as defined in that
BC's `<session_management>` section.
After the commit lands, emit the M{N+1} handoff block from
`docs/implementation-prompts/_handoff-template.md` per `_shared.md § 10`
before ending the session — substitute `{BC}` and `{N+1}` for me so I can
paste it straight into a fresh session.
