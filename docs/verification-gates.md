# Verification gates

The **non-negotiable exit conditions** for any change. There is no "done" claim until every gate that **applies to what you changed** is green **and its actual output is pasted into the session summary** — the real output, not a description. This file is the single source; prompts reference it and append only their unit-specific smoke checks.

**Always — build, restore, format:**

```bash
dotnet build -m
dotnet restore --locked-mode
dotnet format whitespace --no-restore --verify-no-changes
dotnet format style --no-restore --verify-no-changes
```

**Tests — the projects covering what you changed.** A BC dispatch runs the full four-project quartet; a cross-cutting change runs the relevant set.

```bash
dotnet test test/{Unit}.UnitTests/
dotnet test test/{Unit}.ArchitectureTests/
dotnet test test/{Unit}.IntegrationTests/
dotnet test test/{Unit}.FunctionalTests/
```

**Compose health — only for changes that touch a container's runtime** (matching `.claude/rules.md`'s "for containerized changes"); a docs- or config-only change skips it.

```bash
docker compose --profile full up -d   # the unit's container starts + healthcheck passes
```

- **Locked-mode** restore fails if a package was added without regenerating the lock file — see the approved add procedure in `docs/implementation-prompts/_shared.md § 3`.
- Each dispatch prompt appends its **unit-specific smoke checks** (e.g. a `curl` of a new endpoint) after these gates.
- This is **Role 2** of the review stack (`_shared.md § 11`), run via `superpowers:verification-before-completion`.
