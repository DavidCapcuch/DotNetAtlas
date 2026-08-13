# CLAUDE.md

Standing behavioral rules: @.claude/rules.md

DotNetAtlas is a .NET reference eShop — bounded contexts, a BFF and a centralized checkout saga,
event-driven over Kafka with a transactional outbox.

## Build & Restore

```bash
dotnet build -m
dotnet restore --locked-mode
```

## Local Infrastructure

```bash
docker compose --profile core up -d    # datastores + the mail and blob stubs, nothing else
docker compose --profile full up -d    # everything, including the services themselves
```

- **Reach for `full` unless you only need a datastore.** Kafka, Schema Registry, Keycloak, the
  outbox relays, the services and the observability stack are all `full`-only. Which profile each
  gate needs, and why: `.claude/verification-gates.md`.

## Integration tests

- **Cap integration tests at 4 xUnit threads** — `test/xunit.runner.json` says `unlimited`, so the
  cap is an override you must pass:

```bash
dotnet test <proj> --no-build --blame-hang-timeout 10m -- xUnit.MaxParallelThreads=4
```

**Known flake amplifier:** the local container engine's relay can wedge under port-forward churn. Testcontainers' random host ports *are* that churn and concurrency multiplies it.

- **If flakes worsen, drop the cap before suspecting code.** Engine-specific recovery is a per-machine concern, not a repo one.

## Agent skills

- **Issues** live on GitHub (`DavidCapcuch/DotNetAtlas`); skills use the `gh` CLI. Recipes, the
  triage label strings (**used as-is, never remapped**), and the edit-the-body-not-a-comment rule:
  `.claude/issue-tracker.md`.
- **Domain docs** — multi-context repo; `.claude/domain-docs.md` maps which docs exist, where ADRs
  live, and the reading order.
- **Proceed silently when `CONTEXT.md` / `CONTEXT-MAP.md` is missing** — `/grill-with-docs` creates
  them lazily.

## Conventions

- **A new `services/<BC>/` is 4-layer** — `.Domain` / `.Application` / `.Infrastructure` / `.Api`.
- **Never add a missing test-project kind unasked** — the absence is a choice, not a gap.
- **Migrations are generated, never hand-written** — the EF migration and the `V*.sql` script are
  agent-deny-protected in `.claude/settings.json`. Commands and the dev/test/deployed split:
  `.claude/migrations.md`.
- **Never hand-edit the generated `.cs` beside an `.avsc`.** Messaging contracts are Avro schemas in
  `platform/Platform.SchemaRegistry.Contracts`; regeneration and the commit rule: `.claude/avro.md`.
- **Functional core, imperative shell** — this is DDD, preferring domain model **purity +
  performance** over completeness.
  - **A rule decidable from aggregate state alone stays in the domain.**
  - **Application handlers do the out-of-process reading** — fetch what a decision needs, then pass
    it into the domain.
- **Look a cross-cutting convention up; never infer it from a nearby file.**
  `docs/bc-design/conventions.md` § 8 maps each topic to its canonical doc.

## Project status

- **Non-production reference solution — breaking changes are always allowed**, including ADRs, which can be rewritten inline after the fact. Target profile: `docs/adr/0009-reference-solution-target-profile.md`.
- **BFF service (`src/EShop.BFF/`) is under active build** — `src/EShop.BFF/EShop.BFF.Api/Endpoints/` shows what is wired today. **Checkout and order-summary are not built yet.**
