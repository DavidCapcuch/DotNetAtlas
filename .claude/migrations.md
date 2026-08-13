# Database migrations

Every artifact below is **agent-deny-protected** in `.claude/settings.json` — the commands produce
the final form, so none of it is ever hand-edited. An agent that needs a change regenerates it.

## EF migration

Generate with `dotnet ef migrations add`; never hand-write the `.cs` from scratch. This produces the
migration, its `*.Designer.cs`, and the updated `*ModelSnapshot.cs` under
`Persistence/Database/Migrations/`.

## SQL script

Emit with **both** `--idempotent` and `--no-transactions`. Flyway and Evolve each wrap a script in
their own transaction, so a script carrying its own would nest:

```bash
dotnet ef migrations script <from> <to> --idempotent --no-transactions \
  --project services/<BC>/<BC>.Infrastructure \
  --startup-project services/<BC>/<BC>.Api \
  --output services/<BC>/<BC>.Infrastructure/Persistence/Database/Migrations/SqlScripts/V###__<Name>.sql
```

## Which mechanism runs where

Development applies the EF model via `MigrateAsync`, Development-gated in
`Platform.ServiceDefaults/MigrationStartupExtensions`. Testing replays the committed `V*.sql`
through **Evolve** (`Platform.Test.Framework/Database/PostgreSqlTestContainer`). Deployed replays
the same `V*.sql` through **Flyway** (the compose `flyway` service).

**So test fixtures never call `MigrateAsync` / `EnsureCreatedAsync`** — they exercise the exact SQL
that production runs.
