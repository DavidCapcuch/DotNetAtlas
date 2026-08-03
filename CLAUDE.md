# CLAUDE.md

Standing behavioral rules (loaded every session): @.claude/rules.md

## Repository layout

Solution: `DotNetAtlas.slnx`. Four source trees plus tests:

- **`services/<BC>/`** — 7 bounded contexts (Basket, Catalog, Inventory, Invoicing, Notifications, Ordering, Payments); each is 4-layer `.Domain` / `.Application` / `.Infrastructure` / `.Api`.
- **`platform/`** — shared libraries (SharedKernel, ServiceDefaults, CQRS, reliable-messaging Inbox/Outbox, KafkaFlow extensions, `SchemaRegistry.Contracts`, Test.Framework).
- **`src/`** — `EShop.BFF/` (`.Api` / `.Infrastructure`); also infra config dirs (keycloak, postgres, grafana, prometheus, otel-collector, nginx-cdn).
- **`saga/`** — `SagaOrchestrators` (centralized checkout saga).
- **`test/`** — one trio per unit: `{Unit}.UnitTests` / `.IntegrationTests` / `.ArchitectureTests` (saga keeps its `SagaOrchestrators.UnitTests` / `.IntegrationTests` pair).

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

## Worktrees

Procedure lives in the `daca-dotnet-worktrees` skill. Repo-specific constraints:

- **Integration tests are parallel-safe — run them capped at 4 xUnit threads.**

```bash
dotnet test <proj> --no-build --blame-hang-timeout 10m -- xUnit.MaxParallelThreads=4
```

**Singletons — per machine, one worktree at a time:**
- `docker compose --profile core|full` — **do not parameterize the ports to work around this.** 38 fixed `container_name:` and 35 fixed host ports collide daemon-wide; one stack is sized at 8 CPU / 32 GB.
- `dotnet run` / `preview_start` — `launchSettings.json` pins 5100–5108; `.claude/launch.json` pins 5104/5105/5106/65410/65420.
- The `daca-gates` container-health and smoke-check steps, which depend on both of the above.

**Known flake amplifier:** the Rancher Desktop WSL relay wedges with `WSAENOBUFS` under port-forward churn (memory `windows-integration-fixture-flaky-rerun`). Testcontainers' random host ports *are* that churn and concurrency multiplies it.
- **Recovery:** `wsl --terminate rancher-desktop`, then restart the app.
- **If flakes worsen, drop the cap before suspecting code.**

## Formatting (CI-enforced)

```bash
dotnet format whitespace --no-restore --verify-no-changes
dotnet format style --no-restore --verify-no-changes
```

## Non-obvious Conventions

- **Central Package Management** — package versions are centralized in `Directory.Packages.props` at the `services/`, `saga/`, `platform/`, `src/`, and `test/` levels; add packages to the correct level's file.
  - **Never put a `Version=` on a `PackageReference`.**
- **EF Core migrations** — generate via `dotnet ef migrations add`; never hand-write the `.cs` migration from scratch.
  - `*ModelSnapshot.cs` and `*.Designer.cs` are **agent-deny-protected**.
- **SQL-script migrations** (`V*.sql` under each BC's `Persistence/Database/Migrations/SqlScripts/`) — emit with **both** `--idempotent` and `--no-transactions`: Flyway and Evolve both wrap each script in their own transaction.
  - `V*.sql` is likewise **agent-deny-protected** in `.claude/settings.json` — generate it with the command below; the flags produce the final form, so it is not hand-edited.
  ```bash
  dotnet ef migrations script <from> <to> --idempotent --no-transactions \
    --project services/<BC>/<BC>.Infrastructure \
    --startup-project services/<BC>/<BC>.Api \
    --output services/<BC>/<BC>.Infrastructure/Persistence/Database/Migrations/SqlScripts/V###__<Name>.sql
  ```
- **Functional core, imperative shell** — this is DDD, preferring domain model **purity + performance** over completeness.
  - **Domain types never touch out-of-process dependencies** — application handlers read what a decision needs and pass it in.
  - **A rule decidable from aggregate state alone stays in the domain.**
- **Use the result pattern for expected errors** — reserve exceptions only for exceptional situations.
- **Define event-driven messaging contracts as Avro schemas** in `platform/Platform.SchemaRegistry.Contracts`.
- **Avro C# bindings** (`.cs` files next to `.avsc`) — never hand-edit.
  - Regenerate via `platform/Platform.SchemaRegistry.Contracts/generate-avro.ps1 <path-to-schema.avsc>` (wraps `dotnet tool` `Apache.Avro.Tools` avrogen) after every `.avsc` edit.
  - Commit both the `.avsc` and the regenerated `.cs` together.
  - The script runs `dotnet tool restore` against the pinned local manifest (`.config/dotnet-tools.json`), so every dev/CI machine uses the same `Apache.Avro.Tools` version — no global install required.

## Agent skills

### Issue tracker

Issues live on GitHub (`DavidCapcuch/DotNetAtlas`); skills use the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Five canonical roles (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`) — default vocabulary, no remapping. See `docs/agents/triage-labels.md`.

### Domain docs

Multi-context repo. See `docs/agents/domain.md`.

- `CONTEXT-MAP.md` at the root points to per-bounded-context `CONTEXT.md` files (one per service).
- System-wide ADRs live in `docs/adr/`; context-scoped ADRs in `services/<context>/docs/adr/`.
- **Files are created lazily by `/grill-with-docs` — proceed silently if any are missing.**

## Project status

- **Non-production reference solution — breaking changes are always allowed**, including ADRs, which can be rewritten inline after the fact.
- **BFF service (`src/EShop.BFF/`) is under active build** — implemented: the read-side pages (home, product, basket), the buyer-scoped RFC 8693 token exchange, and the basket mutation forwarders. Not yet built: checkout and order-summary.
