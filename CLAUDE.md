# CLAUDE.md

Standing behavioral rules (loaded every session): @.claude/rules.md

## Repository layout

Solution: `DotNetAtlas.slnx`. Four source trees plus tests:

- **`services/<BC>/`** — 7 bounded contexts (Basket, Catalog, Inventory, Invoicing, Notifications, Ordering, Payments); each is 4-layer `.Domain` / `.Application` / `.Infrastructure` / `.Api`.
- **`platform/`** — shared libraries (SharedKernel, ServiceDefaults, CQRS, reliable-messaging Inbox/Outbox, KafkaFlow extensions, `SchemaRegistry.Contracts`, Test.Framework).
- **`src/`** — `DotNetAtlas.*` reference service (4-layer) and `EShop.BFF/` (`.Api` / `.Infrastructure`); also infra config dirs (keycloak, postgres, grafana, prometheus, otel-collector, nginx-cdn).
- **`saga/`** — `SagaOrchestrators` (centralized checkout saga).
- **`test/`** — one quartet per unit: `{Unit}.UnitTests` / `.ArchitectureTests` / `.IntegrationTests` / `.FunctionalTests`.

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

- **Package versions:** Centralized in `Directory.Packages.props` at the `services/`, `saga/`, `platform/`, `src/`, and `test/` levels — add packages to the correct level's file
- EF Core migrations: generate via `dotnet ef migrations add` (never hand-write the `.cs` migration from scratch). After generation, inspect the `Up()` / `Down()` and fix EF's choices where they would destroy data — typically swap `DropColumn` + `AddColumn` for `RenameColumn` on column renames. The schema-snapshot files (`*ModelSnapshot.cs`, `*.Designer.cs`) are tool-managed; let `dotnet ef` regenerate them. These generated files — the migration `.cs`, snapshot, and `.Designer.cs` — are **agent-deny-protected** in `.claude/settings.json`: the agent runs the `dotnet ef` commands but does not hand-edit the files, so any data-preserving `Up()`/`Down()` adjustment (the `RenameColumn` swap above) is a **human** step.
- **SQL-script migrations** (`V*.sql` under each BC's `Persistence/Database/Migrations/SqlScripts/`): emit with **both** `--idempotent` and `--no-transactions` — Flyway and Evolve both wrap each script in their own transaction, so any `START TRANSACTION;` / `COMMIT;` inside the script produces noisy "transaction already in progress" warnings and a non-zero nested commit. Idempotent guards (`DO $EF$ BEGIN IF NOT EXISTS(... __EFMigrationsHistory ...) THEN ... END IF; END $EF$;`) stay; the `--no-transactions` flag keeps the outer transaction wrappers out of the generated script. `V*.sql` is likewise **agent-deny-protected** in `.claude/settings.json` — generate it with the command below; the flags produce the final form, so it is not hand-edited.
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
- **Deletion-heavy work: close Rider (the IDE) first.** It auto-reconciles `DotNetAtlas.slnx` on bulk file deletion and silently drops *unrelated* projects + their `ProjectReference`s (and recreates deleted dirs) — which drops tests from CI. After any bulk delete, diff the slnx project set vs HEAD. See memory `ide-slnx-reconcile-corruption`.

## Agent skills

### Issue tracker

Issues live on GitHub (`DavidCapcuch/DotNetAtlas`); skills use the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Five canonical roles (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`) — default vocabulary, no remapping. See `docs/agents/triage-labels.md`.

### Domain docs

Multi-context repo. `CONTEXT-MAP.md` at the root points to per-bounded-context `CONTEXT.md` files (one per service); system-wide ADRs live in `docs/adr/`, context-scoped ADRs in `services/<context>/docs/adr/`. Files are created lazily by `/grill-with-docs` — proceed silently if any are missing. See `docs/agents/domain.md`.


This is a non-production reference solution. Breaking changes are always allowed. Including ADRs, which can be rewritten inline after the fact.

BFF service (`src/EShop.BFF/`) is under active build — the read-side pages (home, product, basket) and the buyer-scoped RFC 8693 token exchange are implemented; basket mutations, checkout, and order-summary are not yet built.
