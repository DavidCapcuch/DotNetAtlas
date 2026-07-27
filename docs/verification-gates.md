# Verification gates — repo deltas

The gate set itself — locked restore, build, format, the changed unit's test projects, container health, smoke check — plus its flags, its fail-fast order, and the rule that **only pasted actual output counts as evidence** are the `daca-gates` skill's. This file carries only what is specific to this repo.

- **Compose profile** — the services sit behind compose profiles, so the container-health gate needs `--profile full`; a bare `up` starts nothing relevant and exits 0.
- **A locked-mode restore failure** means a package was added without regenerating the lock file — follow the approved add procedure in [`_shared.md § 3`](implementation-prompts/_shared.md).
- **Smoke checks are per-dispatch, not central** — each dispatch prompt appends its own (e.g. a `curl` of a new endpoint) after the standard gates; `bff.md`'s `<verification>` block is the worked example.
- This is **Role 2** of the review stack (`_shared.md § 11`), run via `superpowers:verification-before-completion`.
