# CLAUDE.md

Standing behavioral rules (loaded every session): @.claude/rules.md

## Repository layout

Solution: `DotNetAtlas.slnx`. Source lives under `services/`, `platform/`, `src/` and `saga/`;
`ls -d services/*/ platform/*/ src/*/ saga/*/ test/*/` lists what each holds today.

- **`services/<BC>/`** — one folder per bounded context, each a 4-layer `.Domain` / `.Application` / `.Infrastructure` / `.Api`.
- **`platform/`** — shared libraries, with their unit tests beside them; platform architecture tests live in `test/`.
- **`src/`** — the BFF, alongside one config directory per infra component.
- **`saga/`** — the centralized checkout saga, with both its test projects beside it.
- **`test/`** — the remaining test projects, named `{Unit}.UnitTests` / `.IntegrationTests` / `.ArchitectureTests` / `.FunctionalTests`. Not every unit has all four.

## Build & Restore

```bash
dotnet build -m
dotnet restore --locked-mode
```

Restore requires `--locked-mode` — lock files are committed and CI enforces them.

- **`packages.lock.json` is agent-deny-protected** — never edit it. Regenerate it by running `dotnet restore` once **without** `--locked-mode`, then commit the lock delta.

## Local Infrastructure

```bash
docker compose --profile core up -d    # just the backing dependencies the services need
docker compose --profile full up -d    # everything, including the services themselves
```

## Worktrees

Procedure lives in the `daca-dotnet-worktrees` skill. Repo-specific constraints:

- **Integration tests are parallel-safe — run them capped at 4 xUnit threads.**

```bash
dotnet test <proj> --no-build --blame-hang-timeout 10m -- xUnit.MaxParallelThreads=4
```

**Singletons — per machine, one worktree at a time:**
- `docker compose --profile core|full` — services pin fixed `container_name:` values and fixed host ports, so a second stack collides daemon-wide; one stack is sized at 8 CPU / 32 GB. **Serialize the stack rather than reassigning its ports.**
- `dotnet run` / `preview_start` — the projects' `launchSettings.json` files and `.claude/launch.json` pin fixed host ports, and the two sets overlap; read them for the current values.
- The `daca-gates` container-health and smoke-check steps, which depend on both of the above.

**Known flake amplifier:** the Rancher Desktop WSL relay wedges with `WSAENOBUFS` under port-forward churn. Testcontainers' random host ports *are* that churn and concurrency multiplies it.
- **Recovery:** `wsl --terminate rancher-desktop`, then restart the app.
- **If flakes worsen, drop the cap before suspecting code.**

## Formatting (CI-enforced)

```bash
dotnet format whitespace --no-restore --verify-no-changes
dotnet format style --no-restore --verify-no-changes
```

## Non-obvious Conventions

- **Docs state rules and navigation; the repo answers the values.** When the repo itself answers it — the bounded contexts, the test projects, the pinned ports — name the file or command instead of the answer. A value copied into prose drifts silently: `validate-documentation-links.yml` validates *links* and nothing else, so a stale enumeration or a wrong path in a code span sails through CI.
  - **A table a currently-accepted ADR designates as the single source of truth stays** — keep it and cite that ADR inline. Search the ADR corpus for the designation rather than assuming one; ADR-0033 is one such (`docs/bc-design/events-catalog.md` §2 and `docs/kafka-topology.md`), not the only one.
  - **ADR bodies and `docs/research/*` bodies are point-in-time records** — they state what was true and what was decided when written, so leave them standing rather than reconciling them to today.
- **Central Package Management** — package versions live in `Directory.Packages.props`; add the package to the nearest one above your project.
  - **A `PackageReference` carries the package name only — the version belongs in `Directory.Packages.props`.**
- **EF Core migrations** — generate via `dotnet ef migrations add`; never hand-write the `.cs` migration from scratch.
  - Every `.cs` under `Persistence/Database/Migrations/` is **agent-deny-protected** — the migration, its `*.Designer.cs`, and `*ModelSnapshot.cs`.
- **SQL-script migrations** (`V*.sql` under each BC's `Persistence/Database/Migrations/SqlScripts/`) — emit with **both** `--idempotent` and `--no-transactions`: Flyway and Evolve both wrap each script in their own transaction.
  - `V*.sql` is likewise **agent-deny-protected** in `.claude/settings.json` — the flags produce the final form, so it is never hand-edited.
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
  - The script restores the pinned manifest (`.config/dotnet-tools.json`), so every machine uses the same `Apache.Avro.Tools` version — no global install required.

## Agent skills

### Issue tracker

Issues live on GitHub (`DavidCapcuch/DotNetAtlas`); skills use the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

The skills' canonical triage roles map 1:1 onto this repo's label strings — use them as-is, no remapping. `docs/agents/triage-labels.md` holds the roles and what each one means.

### Domain docs

Multi-context repo. `docs/agents/domain.md` is the map — which domain docs exist, where ADRs live, and the order to read them in.

- **`CONTEXT.md` / `CONTEXT-MAP.md` are created lazily by `/grill-with-docs`, so proceed silently when one is missing.**

## Project status

- **Non-production reference solution — breaking changes are always allowed**, including ADRs, which can be rewritten inline after the fact.
- **BFF service (`src/EShop.BFF/`) is under active build** — `src/EShop.BFF/EShop.BFF.Api/Endpoints/` shows what is wired today. **Checkout and order-summary are not built yet.**
