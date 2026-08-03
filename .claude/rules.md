# Standing rules

Non-negotiable behavioral constraints for every session. The conventions + rationale they build on live in `CLAUDE.md` and `docs/bc-design/`.

- **Evidence before assertions.** Don't claim "done" without pasting the actual output of the **verification gates** — run them via the `daca-gates` skill, which owns the gate set and its flags; this repo's deltas are in `docs/verification-gates.md`.
- **Ship every new behavior with new tests.** New behavior is not "done" on green pre-existing tests alone.
- **Ask, don't guess** — when uncertain about a cross-BC contract, an ADR, or an architectural seam. A 60-second pause beats a silent wrong assumption.
- **Never force-push. Commit / push only when asked.**
- **Solo repo — no PRs**, so the gates above are the only review a change gets.
- **Work on a branch in a worktree, never directly on `main`.**
  - Land it from the primary checkout: `git switch main` then `git merge --ff-only <branch>`.
  - **`--ff-only` is the guard, not a formality** — if it refuses, `main` moved, so `git fetch && git rebase origin/main` on the branch and retry.
  - **Never plain `git merge`** — a merge bubble is exactly what this keeps out of a linear history.
- **Commit by explicit pathspec** (`git commit <path> …`), never bare `git add` — it sweeps unrelated staged work.
- **Run parallel sessions in worktrees**, never in one shared tree — CLAUDE.md's Worktrees section holds the caps and the singletons.
