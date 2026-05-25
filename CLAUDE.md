# CLAUDE.md

## Build & Restore

```bash
dotnet build -m
dotnet restore --locked-mode
```

Restore requires `--locked-mode` — lock files are committed and CI enforces them.

## Local Infrastructure

```bash
docker compose --profile core up -d    # DB + Redis only
docker compose --profile full up -d    # All services (Jaeger, Seq, Kafka, etc.)
```

## Formatting (CI-enforced)

```bash
dotnet format whitespace --no-restore --verify-no-changes
dotnet format style --no-restore --verify-no-changes
```

## Non-obvious Conventions

- **Package versions:** Centralized in `Directory.Packages.props` at root, `services/`, `saga/`, `platform/`, and `test/` levels — add packages to the correct level's file
- EF Core migrations: generate via `dotnet ef migrations add` (never hand-write the `.cs` migration from scratch). After generation, inspect the `Up()` / `Down()` and fix EF's choices where they would destroy data — typically swap `DropColumn` + `AddColumn` for `RenameColumn` on column renames. The schema-snapshot files (`*ModelSnapshot.cs`, `*.Designer.cs`) are tool-managed; let `dotnet ef` regenerate them.
- **SQL-script migrations** (`V*.sql` under each BC's `Persistence/Database/Migrations/SqlScripts/`): emit with **both** `--idempotent` and `--no-transactions` — Flyway and Evolve both wrap each script in their own transaction, so any `START TRANSACTION;` / `COMMIT;` inside the script produces noisy "transaction already in progress" warnings and a non-zero nested commit. Idempotent guards (`DO $EF$ BEGIN IF NOT EXISTS(... __EFMigrationsHistory ...) THEN ... END IF; END $EF$;`) stay; only the outer transaction wrappers go.
  ```bash
  dotnet ef migrations script <from> <to> --idempotent --no-transactions \
    --project services/<BC>/<BC>.Infrastructure \
    --startup-project services/<BC>/<BC>.Api \
    --output services/<BC>/<BC>.Infrastructure/Persistence/Database/Migrations/SqlScripts/V###__<Name>.sql
  ```
- Codebase follows DDD and prefers domain model completeness + performance (sacrificing purity)
- Codebase uses result pattern for expected errors and reserves exceptions only for exceptional situations
- Codebase uses Avro schemas as contracts for event-driven messaging stored in platform/Platform.SchemaRegistry.Contracts
- **Avro C# bindings (`.cs` files next to `.avsc`):** never hand-edit. They are regenerated via `platform/Platform.SchemaRegistry.Contracts/generate-avro.ps1 <path-to-schema.avsc>` (wraps `dotnet tool` `Apache.Avro.Tools` avrogen). Run after every `.avsc` edit; commit both the `.avsc` and the regenerated `.cs` together. If avrogen isn't installed the script `dotnet tool install`s it on first run.
- **Platform.SharedKernel contract changes:** any edit that adds or modifies `required` members, base-class signatures, or other compile-time contracts on shared-kernel types MUST be verified with `dotnet build -m` solution-wide before commit. Slice builds (Domain-only / one-BC-only) do not surface CS9035 violations in downstream BC trees and have historically broken the build (see #138 / commit 8616fe1).

## Testcontainers + corporate proxy on Windows

If `dotnet test` against any `*.IntegrationTests` project fails inside the fixture constructor with:

```
DockerUnavailableException : Failed to connect to Docker endpoint at 'npipe://./pipe/docker_engine'.
... System.InvalidOperationException : This operation is not supported for a relative URI.
```

— even though `docker info` works in the same shell — the cause is `HTTP_PROXY` / `HTTPS_PROXY` set by the corporate environment: the `npipe://` URI cannot be parsed by `HttpClient`'s env-proxy resolver, and Docker.DotNet routes the named-pipe call through that resolver.

Two workarounds, in preferred order:

```bash
# A) RECOMMENDED — strip the proxy from the invocation entirely. 100% reliable on corporate-proxy hosts where option B (NO_PROXY='*') actually fails: the npipe:// URI is parsed by HttpClient's env-proxy resolver BEFORE NO_PROXY is consulted, so the relative-URI exception still fires.
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet test path/to/IntegrationTests.csproj

# B) ALTERNATIVE — per-command bypass; only works on hosts where NO_PROXY is honoured by HttpClient ahead of the env-proxy resolver. Use when other commands in the same shell still need the proxy.
NO_PROXY='*' dotnet test path/to/IntegrationTests.csproj
```

Shell state does not persist between separate `Bash` tool calls (each invocation re-sources the user profile), so the bypass must be chained into every `dotnet test` command — not run as a standalone setup step.

## Agent skills

### Issue tracker

Issues live on GitHub (`DavidCapcuch/DotNetAtlas`); skills use the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Five canonical roles (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`) — default vocabulary, no remapping. See `docs/agents/triage-labels.md`.

### Domain docs

Multi-context repo. `CONTEXT-MAP.md` at the root points to per-bounded-context `CONTEXT.md` files (one per service); system-wide ADRs live in `docs/adr/`, context-scoped ADRs in `services/<context>/docs/adr/`. Files are created lazily by `/grill-with-docs` — proceed silently if any are missing. See `docs/agents/domain.md`.


This is a non-production reference solution. Breaking changes are always allowed.
