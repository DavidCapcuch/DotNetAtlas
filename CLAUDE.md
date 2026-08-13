# CLAUDE.md

DotNetAtlas is a .NET reference eShop — bounded contexts, a BFF and a centralized checkout saga,
event-driven over Kafka with a transactional outbox.

## Standing rules

- **Ask, don't guess** — when uncertain about a cross-BC contract, an ADR, or an architectural seam.
  A 60-second pause beats a silent wrong assumption.
- **The gates are the only review a change gets** — solo repo, no PRs. Repo gate deltas — compose
  profile per gate, the thread cap's why, the flake response: `.claude/verification-gates.md`.
- **Breaking changes are always allowed** — non-production reference solution; ADRs included.
  - **Rewrite an ADR inline when the decision changes** — never to reconcile a body to today's state.
  - Target profile: `docs/adr/0009-reference-solution-target-profile.md`.
- **Restore with `dotnet restore --locked-mode`, and build `--no-restore`** — this repo generates
  `packages.lock.json`, so a bare restore silently rewrites it and the `settings.json` deny rule
  does not cover Bash.

## Local Infrastructure

- **Narrow to `--profile core` only when the datastores and stubs are all you need** — `.env` sets
  `COMPOSE_PROFILES=full`, so a bare `up` starts everything:

```bash
docker compose --profile core up -d
```

## Integration tests

- **Cap integration tests at 4 xUnit threads** — `test/xunit.runner.json` says `unlimited`, so pass
  it on every run.
  - **VSTest-adapter form** — the repo pins `xunit.runner.visualstudio`, and these flags are
    silently ignored under Microsoft.Testing.Platform.
  - **`10m`, not the `daca-gates` default `5m`** — parallel sessions are the norm here.

```bash
dotnet test <proj> --no-build --blame-hang-timeout 10m -- xUnit.MaxParallelThreads=4
```

## Agent skills

- **Tickets live as GitHub issues**; skills use `gh`. Recipes, the triage label strings, and the
  edit-the-body-not-a-comment rule: `.claude/issue-tracker.md`.
  - **Use the triage label strings as-is, never remapped.**
- **Domain docs** — multi-context repo; `.claude/domain-docs.md` maps the per-context docs, ADR
  locations and reading order.
- **Proceed silently when `CONTEXT.md` / `CONTEXT-MAP.md` is missing** — `domain-modeling` creates
  them lazily. Never flag the absence or offer to create one.

## Conventions

- **Look a cross-cutting convention up in `docs/bc-design/conventions.md` § 8; never infer one from
  a nearby file** — § 8 maps each topic to its canonical doc.
- **Never add a missing test-project kind unasked** — the absence is a choice, not a gap.
- **Migrations are generated, never hand-written** — commands and the dev/test/deployed split:
  `.claude/migrations.md`.
- **Never hand-edit the generated `.cs` beside an `.avsc`.** Messaging contracts are Avro schemas in
  `platform/Platform.SchemaRegistry.Contracts`; regeneration and the commit rule: `.claude/avro.md`.
- **Functional core, imperative shell** — a rule that needs I/O to decide never lives in the domain;
  the handler fetches, then passes values in. **The core is the existing domain types, not a new
  pure layer** — a mutable aggregate counts; statefulness isn't impurity, hidden inputs are. The
  trade-off this settles, and the placement rules: `docs/bc-design/conventions.md` § 7.

## Project status

- **Checkout and order-summary are not built yet** — the BFF service (`src/EShop.BFF/`) is under
  active build; `src/EShop.BFF/EShop.BFF.Api/Endpoints/` shows what is wired today.
