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

## Testcontainers + corporate proxy on Windows

If `dotnet test` against any `*.IntegrationTests` project fails inside the fixture constructor with:

```
DockerUnavailableException : Failed to connect to Docker endpoint at 'npipe://./pipe/docker_engine'.
... System.InvalidOperationException : This operation is not supported for a relative URI.
```

— even though `docker info` works in the same shell — the cause is `HTTP_PROXY` / `HTTPS_PROXY` set by the corporate environment: the `npipe://` URI cannot be parsed by `HttpClient`'s env-proxy resolver, and Docker.DotNet routes the named-pipe call through that resolver.

Two equivalent workarounds; pick the one that fits what else you're doing in the same shell:

```bash
# A) Per-command bypass — recommended when other commands in the shell still need the proxy
NO_PROXY='*' dotnet test path/to/IntegrationTests.csproj

# B) Strip the proxy from the invocation entirely — use when the shell is dedicated to tests
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet test path/to/IntegrationTests.csproj
```

Shell state does not persist between separate `Bash` tool calls (each invocation re-sources the user profile), so the bypass must be chained into every `dotnet test` command — not run as a standalone setup step.
