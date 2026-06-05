# CLAUDE.md

Standing behavioral rules (loaded every session): @.claude/rules.md

## Build & Restore

```bash
dotnet build -m
dotnet restore --locked-mode
```

Restore requires `--locked-mode` — lock files are committed and CI enforces them.

## Local Infrastructure

```bash
docker compose --profile core up -d    # Postgres + redis-basket + Azurite (storage deps only)
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
- **Avro C# bindings (`.cs` files next to `.avsc`):** never hand-edit. They are regenerated via `platform/Platform.SchemaRegistry.Contracts/generate-avro.ps1 <path-to-schema.avsc>` (wraps `dotnet tool` `Apache.Avro.Tools` avrogen). Run after every `.avsc` edit; commit both the `.avsc` and the regenerated `.cs` together. The script runs `dotnet tool restore` against the pinned local manifest (`.config/dotnet-tools.json`), so every dev/CI machine uses the same `Apache.Avro.Tools` version — no global install required.
- **`src/Weather` is reference scaffolding, not production code** — it predates the current conventions (e.g. still uses Ardalis.Specification on the read side) and is slated for deletion. Do **not** flag ADR violations, over-fetch, or other issues in `src/Weather`; treat it as an illustrative template only.

## Agent skills

### Issue tracker

Issues live on GitHub (`DavidCapcuch/DotNetAtlas`); skills use the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Five canonical roles (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`) — default vocabulary, no remapping. See `docs/agents/triage-labels.md`.

### Domain docs

Multi-context repo. `CONTEXT-MAP.md` at the root points to per-bounded-context `CONTEXT.md` files (one per service); system-wide ADRs live in `docs/adr/`, context-scoped ADRs in `services/<context>/docs/adr/`. Files are created lazily by `/grill-with-docs` — proceed silently if any are missing. See `docs/agents/domain.md`.


This is a non-production reference solution. Breaking changes are always allowed. Including ADRs, which can be rewritten inline after the fact.

BFF service is not yet started and implemented.
