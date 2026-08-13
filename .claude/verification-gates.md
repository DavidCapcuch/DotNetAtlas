# Verification gates — repo deltas

The gate set itself — locked restore, build, format, the changed unit's test projects, container
health, smoke check — plus its flags, its fail-fast order, and the rule that **only pasted actual
output counts as evidence** are the `daca-gates` skill's. This file carries only what is specific
to this repo — the why and the exceptions.

**The format gate needs no manual run** — a `Stop` hook (`.claude/hooks/format-changed.ps1`) formats
every changed `.cs` at the end of each turn. That hook is the only enforcement on this workflow:
`pr-enforce-format.yml` runs from `pr-ci.yml` on `pull_request`, and this repo lands via
`merge --ff-only` without PRs. Verify explicitly only when auditing the hook itself:

```bash
dotnet format whitespace --no-restore --verify-no-changes
dotnet format style --no-restore --verify-no-changes
```

- **Compose profile** — `.env` sets `COMPOSE_PROFILES=full`, so a bare `up` already starts the whole stack the container-health gate needs. `--profile core` on the command line overrides that down to the datastores and stubs.
- **A locked-mode restore failure** means a package was added without regenerating the lock file — add it to the nearest `Directory.Packages.props` above the project, run `dotnet restore` once **without** `--locked-mode`, then commit the lock delta.
- **Smoke checks are per-dispatch, not central** — each dispatch prompt appends its own (e.g. a `curl` of a new endpoint) after the standard gates; `docs/implementation-prompts/bff.md`'s `<verification>` block is the worked example.
- This is **Role 2** of the review stack, run via `superpowers:verification-before-completion`.

## Why the integration tests are capped

`test/xunit.runner.json` sets `maxParallelThreads` to `unlimited`, so the 4-thread cap is an
override you pass per run — nothing enforces it for you. The local container engine's port-forward
relay wedges under churn; Testcontainers' random host ports *are* that churn, and concurrency
multiplies it. **If flakes worsen, drop the cap before suspecting code.** Engine-specific recovery
is a per-machine concern, not a repo one.

## Fixed host ports

The compose services pin `container_name:` values and fixed host ports, and the projects'
`launchSettings.json` files overlap with `.claude/launch.json`. Read them for the current values
before binding anything.
