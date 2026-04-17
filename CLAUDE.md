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
- Never touch or generate EF Core/Sql script migrations - always let the user deteministically generate
- Codebase follows DDD and prefers domain model completeness + performance (sacrificing purity)
- Codebase uses result pattern for expected errors and reserves exceptions only for exceptional situations
- Codebase uses Avro schemas as contracts for event-driven messaging stored in platform/Platform.SchemaRegistry.Contracts
