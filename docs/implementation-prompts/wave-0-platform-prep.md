# Master System Prompt — Wave 0: Platform Prep

> Paste this as the first message in a fresh Claude Code session for `C:\Users\david.capcuch\Desktop\Git\DotNetAtlas`.
>
> This is the foundation dispatch. It **must merge before any Wave 1 BC dispatches**.

<thinking_first>
Before writing any code, do these in your **first response** — explicitly, in order:

1. **Read every file under `<reading_order>`** in order. State your understanding of what's locked vs open.
2. **Verify prerequisites.** List anything in `<prerequisites>` that isn't satisfied. STOP and ask if any.
3. **Inventory the existing platform.** Run `ls platform/`, `ls services/`, `ls saga/`, `ls src/`. Confirm what already exists vs what you'll add. Surface any drift from `_shared.md § 5` (Platform libraries).
4. **Confirm applicable ADRs.** For each ADR in `<applicable_adrs>`, name what it implies for Wave 0 platform code (one line each).
5. **State your plan.** Group your `<dod>` items into commit milestones (suggested 8 in `<session_management>`). Confirm with the user before starting code.
6. **Acknowledge stop conditions** from this prompt and `_shared.md § 9`.
</thinking_first>

<mission>
You execute **Wave 0 — the platform-prep PR** that unblocks parallel BC implementation in Wave 1+. Wave 0 is **infrastructure + cross-cutting platform extensions only** — no BC business logic, no aggregates, no use cases. When the session ends, every BC agent can start work without colliding on shared files.

This is one PR. It must build green and pass `dotnet format`, `dotnet test`, and `docker compose --profile full config` before merge.
</mission>

<prerequisites>
- Repository is on a clean branch from `main`.
- Existing services (`services/Catalog/`, `services/Payments/`, `services/Notifications/`, `src/Weather*`) build green.
- Docker + Docker Compose installed; `docker compose --profile core up -d` already works against `main`.

If any of the above is false, STOP and ask.
</prerequisites>

<role_in_system>
Wave 0 is the foundation dispatch. Six BCs and one saga + the BFF will run as parallel sessions in Waves 1–3. Without Wave 0, the parallel sessions would collide on shared files: `docker-compose.yaml` (every BC adds a topic + relay container), `Platform.SharedKernel` (every BC needs `Money` / `Address`), `Platform.ServiceDefaults` (correlation-ID + service-auth + feature-flags), Avro folder structure, schema-compat CI gate, Keycloak realm config (per-service clients). Time is the BCL `TimeProvider`; cross-service HTTP resilience is YARP at the edge — neither requires a platform abstraction.

Wave 0 lands all the shared changes in one atomic PR so Wave 1 BCs can dispatch genuinely in parallel.

**Aspire is explicitly out of scope for Wave 0.** Aspire AppHost wiring will land in a later, ad-hoc session. Do not introduce an AppHost project. Configure all integrations to work in raw `docker-compose` mode; the future Aspire wave will add typed bindings on top.
</role_in_system>

<contract>
LOCKED. Each item below is reviewable as a single checkbox.

**Shared kernel additions (`platform/Platform.SharedKernel/`):**
- `Money` value object — decimal `Amount > 0`, `CurrencyCode` ISO 4217 enum, `Result<Money> Create(decimal, CurrencyCode)`, `+/-` operators with same-currency invariant
- `Address` value object — `Street1`, `Street2?`, `City`, `State?`, `PostalCode`, `CountryCode` (ISO 3166-1 alpha-2)
- Time abstraction per ADR-0015 — use BCL `System.TimeProvider` (auto-registered by the Generic Host); tests substitute `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`. No custom `IClock` interface is shipped.
- DI registration extension: `AddSharedKernel()` on `IServiceCollection`

**`Platform.ServiceDefaults` extensions** (no new project):
- **Correlation-ID middleware** per ADR-0008: `AddCorrelationId()`; ASP.NET middleware reads / generates / validates `X-Correlation-Id` (UUID v7); `DelegatingHandler` for outbound HttpClient; Serilog enricher; OTel `Activity.SetTag("correlation.id", value)` hook
- **Service-to-service auth** per ADR-0010: `AddServiceAuth(serviceName)`; `ClientCredentialsTokenHandler` (caches token until ≤ 30s before expiry); `IHttpClientBuilder.AddServiceAuth(scope)` extension; `AddJwtBearer` config helper for inbound validation
- **Feature flags** per ADR-0014: `AddFeatureFlags(IConfiguration)` → registers OpenFeature SDK + JSON-file provider; `OtelEvaluationHook` emits `Activity` events on every flag evaluation
- **Resilience policy** per ADR-0009: cross-service HTTP resilience is handled by YARP at the edge (retries, timeouts, circuit-breaking). No per-service Polly presets are shipped from `Platform.ServiceDefaults`. Individual SDKs (e.g., `Azure.Storage.Blobs`) may configure their own built-in retry options.
- **Time policy** per ADR-0015: rely on the Generic Host's auto-registered BCL `TimeProvider`; tests swap in `FakeTimeProvider`. `DateTimeOffset` JSON round-trips use the default System.Text.Json ISO-8601 formatting.

**`Platform.KafkaFlow.ProducerHeaders` extension:**
- Producer-side: write `X-Correlation-Id` Kafka header on every produce
- Consumer-side: read `X-Correlation-Id` and bind to `Activity.Current.SetTag` + Serilog `LogContext.PushProperty` for the consumer dispatch duration
- One file added per side; no new project

**`docker-compose.yaml` topology** (under `--profile core` and `--profile full` per existing convention):

8 new Kafka topics (copy from `events-catalog.md § 3`):

| Topic | Partitions | Retention |
|---|---|---|
| `catalog.products` | 3 | infinite |
| `catalog.categories` | 3 | infinite |
| `basket.sessions` | 3 | 30 days |
| `ordering.orders` | 3 | infinite |
| `ordering.order-commands` | 3 | 7 days |
| `inventory.stock-events` | 3 | infinite |
| `inventory.reservations` | 6 | infinite |
| `inventory.reservation-commands` | 3 | 7 days |
| `invoicing.invoices` | 3 | **10 years** (`--config retention.ms=315360000000`) |

Plus rename existing `payments.payments` → `payments.transactions` and `payments.payment-commands` → `payments.commands` (per ADR-0017 chunk-2 decision).

6 outbox-relay containers: `outbox-relay-catalog`, `outbox-relay-basket`, `outbox-relay-ordering`, `outbox-relay-inventory`, `outbox-relay-payments` (renamed from existing relay), `outbox-relay-invoicing`.

Redis split per ADR-0016:
- `redis-basket` (port 6379, AOF `everysec`, `maxmemory-policy noeviction`, persistent volume)
- `redis-cache` (port 6380, no persistence, `maxmemory 512mb`, `maxmemory-policy allkeys-lru`, no volume)

Azurite + nginx-cdn per ADR-0017:
- `azurite` (image `mcr.microsoft.com/azure-storage/azurite:3.31.0`, port 10000, persistent volume)
- `azurite-init` one-shot (creates `invoices` container with 10-year immutable-blob policy)
- `nginx-cdn` (image `nginx:1.27-alpine`, port 8080, mounts `./src/nginx-cdn/nginx.conf`)

**Avro folder skeleton** — empty subfolders only:
- `platform/Platform.SchemaRegistry.Contracts/Avro/{Catalog,Basket,Ordering,Inventory,Payments,Invoicing}/`

**Keycloak realm config** (`src/keycloak/realm-export.json`) per ADR-0010:
- 9 service clients: `catalog-service`, `basket-service`, `ordering-service`, `inventory-service`, `payments-service`, `invoicing-service`, `checkout-saga`, `notifications-service`, `bff` — each `serviceAccountsEnabled: true`, `publicClient: false`
- Scopes per the matrix (documented in `src/keycloak/service-scope-matrix.md` — co-author this companion doc)

**Feature-flag file** (`flags.json` at repo root) seeded with the 3 flags from ADR-0014 — mounted into each service container at `/app/flags.json`.

**Schema-compat CI gate** (`.github/workflows/schema-compat.yml`) per ADR-0007 F-B follow-up: runs Confluent Schema Registry container, registers existing schemas from main, attempts to register PR schemas with the configured compatibility mode, fails on rejection.

**`appsettings.json` connection-string conventions** added per service (only where applicable):
```json
{
  "ConnectionStrings": {
    "Redis:Basket": "redis-basket:6379",
    "Redis:Cache": "redis-cache:6379",
    "AzureStorage": "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://azurite:10000/devstoreaccount1;"
  }
}
```
</contract>

<design_open>
You own these. Justify in your session summary.

- Exact API surface of `Platform.ServiceDefaults` extensions (method names, options shapes)
- OpenFeature .NET package version (latest stable that supports JSON file provider)
- nginx-cdn `proxy_cache_path` size + zone config (the ADR gives reasonable defaults; tune if needed)
- Schema-compat CI gate exact mechanism — shell script vs reusable workflow vs custom GitHub Action
- Outbox-relay containers' resource limits in `docker-compose.yaml` (memory, CPU)
- DLT alert configuration thresholds (the ADR gives `>50/24h` cumulative; if monitoring stack is in scope, wire it; otherwise document for ops)
- README.md updates that describe the new dev-loop topology
</design_open>

<reading_order>
1. `docs/implementation-prompts/_shared.md` — FIRST
2. `docs/eshop-master-design.md` — overall solution shape; especially § 5 (BCs), § 6 (Kafka topics + outbox-relays), § 11 (cross-cutting concerns)
3. **All 12 new ADRs in order:** `docs/adr/0008-correlation-id-propagation.md` → `docs/adr/0019-pdf-generation-questpdf.md`. Each has implementation notes you'll directly translate into code or config.
4. `docs/bc-design/events-catalog.md` § 3 — exact topic-creation block for `docker-compose.yaml`
5. `docs/bc-design/kafka-dlq-strategy.md` — DLT topic naming + alert thresholds
6. `docs/bc-design/architecture-tests.md` — patterns Wave 0 platform code must satisfy
7. **Existing platform inventory** — `ls platform/` and read `Platform.ServiceDefaults`, `Platform.SharedKernel`, `Platform.KafkaFlow.ProducerHeaders` end-to-end before extending
8. `docker-compose.yaml` (current) — understand current containers + volumes + networks before adding
9. `src/keycloak/` — current realm export to understand what's there before adding service clients
10. `CLAUDE.md` — non-negotiable rules
</reading_order>

<applicable_adrs>
Cross-cutting decisions that drive Wave 0 directly. Read each, then apply.

- [ADR-0008](../adr/0008-correlation-id-propagation.md) — implement the middleware + DelegatingHandler + Kafka header propagation; this is the bulk of the `Platform.ServiceDefaults` work
- [ADR-0009](../adr/0009-reference-solution-target-profile.md) — informs partition counts (3/6), retention windows, single-AZ posture; do not over-engineer for HA
- [ADR-0010](../adr/0010-service-to-service-auth.md) — Keycloak realm clients + scopes + `ClientCredentialsTokenHandler`; **ADR-0009 says SASL/OAUTHBEARER on Kafka is out of scope for v1** — do not enable broker-level auth
- [ADR-0011](../adr/0011-pii-handling-gdpr.md) — wire the OTEL allowlist processor + Serilog `[PII]` destructuring policy into `Platform.ServiceDefaults`
- [ADR-0012](../adr/0012-api-versioning.md) — no Wave 0 implementation work, but document the convention so Wave 1 prompts pick it up
- [ADR-0013](../adr/0013-idempotency-key-http.md) — register `Microsoft.AspNetCore.OutputCaching.StackExchangeRedis` package + helper extension; BCs opt-in per endpoint
- [ADR-0014](../adr/0014-feature-flags-openfeature.md) — register OpenFeature SDK + JSON file provider + OTel hook; seed `flags.json`
- [ADR-0015](../adr/0015-time-timezone-policy.md) — rely on BCL `TimeProvider` (auto-registered by Generic Host); no custom clock abstraction or JSON converter needed
- [ADR-0016](../adr/0016-redis-topology.md) — split Redis containers; appsettings naming convention
- [ADR-0017](../adr/0017-blob-storage-cdn.md) — Azurite + nginx-cdn containers; do not introduce Aspire `AddAzureStorage` wiring (deferred)
</applicable_adrs>

<skills>
Universal skills per `_shared.md § 7`. Wave-0-specific:

| Phase | Skill / Agent | When |
|---|---|---|
| Designing the `Platform.ServiceDefaults` extensions surface | `backend-development:api-design-principles` | extension methods are an API for every BC agent — read-heavy on naming/shape. Cite the precedent pattern from the existing surface (e.g., `CorrelationIdServiceCollectionExtensions`) when making the call. |
| Reviewing platform changes before every milestone commit | `Agent(subagent_type="feature-dev:code-reviewer", model="opus")` | **Mandatory** pre-commit on any milestone touching ≥ 5 files. Validated precedent caught one CRITICAL + three IMPORTANT findings. Brief with file list + test list + deferred items. Fix CRITICAL/HIGH before staging. |
| `docker-compose.yaml` ordering of new services | `superpowers:brainstorming` | init containers + dependency ordering have subtle race conditions |
</skills>

<autonomous_evolution>
Wave-0-specific triggers:

- **Production secrets** — Wave 0 commits dev-only Keycloak client secrets in `appsettings.Development.json`. Document this in the session summary; production deployment must rotate.
- **Avro CI gate first run** — the gate may fail-by-default if existing schemas don't match the configured compatibility mode. Run the gate against `main` first; if existing schemas are non-compliant, document the drift and propose a fix (likely "register existing as baseline, then enable enforcement").
- **Outbox-relay resource limits** — if container OOMs are observed during the smoke test, document what limits worked.
- **If a referenced library version doesn't exist** (e.g., OpenFeature .NET 1.x doesn't yet support file provider): pivot to the closest equivalent and document.
- **Azurite immutable-blob policy** — Azurite has historically lagged behind real Azure Blob features on a few edges. If `mc retention set` (or equivalent CLI) doesn't work in the init container, document the gap; v1 can ship without compliance-mode policy in the emulator (production Azure Blob enforces it).
</autonomous_evolution>

<success_criteria>
- A Wave-1 BC agent (Catalog / Basket / Ordering / Inventory / Payments / Invoicing) can dispatch immediately after Wave 0 merges without:
  - Adding a new platform library
  - Modifying `docker-compose.yaml` for shared infra (only adding their own topic + relay)
  - Adding a Keycloak service client (their client already exists)
  - Implementing correlation-id propagation themselves
  - Implementing service-to-service auth handlers themselves
- Every BC's `appsettings.json` has the connection strings it needs.
- The schema-compat CI gate is in place and prevents bad `.avsc` PRs from merging.
- The Aspire AppHost gap is **explicitly documented as deferred** so future contributors don't re-derive the question.
</success_criteria>

<dod>
Concrete deliverables. Extends `_shared.md § 12`.

- [ ] `Platform.SharedKernel` exposes `Money`, `Address`; `AddSharedKernel()` extension; unit tests for VO factories. (Time is the BCL `TimeProvider`, auto-registered by the Generic Host — no custom clock.)
- [ ] `Platform.ServiceDefaults` exposes `AddCorrelationId()`, `AddServiceAuth()`, `AddFeatureFlags()`; OTel PII allowlist processor + Serilog `[PII]` policy wired. No per-service Polly presets (YARP at edge).
- [ ] `Platform.KafkaFlow.ProducerHeaders` carries correlation-id producer + consumer middleware
- [ ] `docker-compose.yaml`: 8 new Kafka topics with explicit retention flags; topic renames `payments.*` → `payments.*`; 6 outbox-relay containers; `redis-basket` + `redis-cache` split; `azurite` + `azurite-init` + `nginx-cdn`; `nginx-cdn` config in `src/nginx-cdn/nginx.conf`
- [ ] `flags.json` at repo root with the 3 seed flags
- [ ] `src/keycloak/realm-export.json` extended with 9 service clients + scopes + `service-scope-matrix.md` companion
- [ ] `.github/workflows/schema-compat.yml` exists and passes against current `main` schemas
- [ ] Avro folder skeleton: `platform/Platform.SchemaRegistry.Contracts/Avro/{Catalog,Basket,Ordering,Inventory,Payments,Invoicing}/` empty subfolders
- [ ] Per-service `appsettings.json` connection-string entries added (only where each service needs them)
- [ ] Each platform extension has at least one unit + integration test (e.g., correlation-id roundtrips through HttpClient → Kafka → consumer)
- [ ] `dotnet build -m`, `dotnet restore --locked-mode`, `dotnet format whitespace`, `dotnet format style`, `dotnet test` all pass
- [ ] `docker compose --profile full config` validates and lists every new container; `docker compose --profile full up -d` starts everything healthy
- [ ] All `<applicable_adrs>` enforced (architecture tests + verification commands)
- [ ] Peer-review chain (`_shared.md § 11`) executed; HIGH findings fixed
</dod>

<boundaries>
**You may write:**
- `platform/Platform.SharedKernel/**`
- `platform/Platform.ServiceDefaults/**`
- `platform/Platform.KafkaFlow.ProducerHeaders/**`
- `docker-compose.yaml`
- `src/nginx-cdn/**` (new folder)
- `src/keycloak/realm-export.json`
- `src/keycloak/service-scope-matrix.md` (new)
- `flags.json`
- `.github/workflows/schema-compat.yml`
- Every existing service's `appsettings*.json` (connection-string entries only — no service code)
- `Directory.Packages.props` (only the new packages from `<contract>`)

**Do not touch:**
- Any `services/{BC}/**` business code (BC agents own those folders)
- Any `.avsc` schema files (BC agents own them)
- Any `saga/SagaOrchestrators/**` (Wave 2 owns this)
- Weather business code
- EF Core migrations (per CLAUDE.md — user-generated only)
- `DotNetAtlas.slnx` adding an Aspire AppHost project (deferred per `<role_in_system>`)
</boundaries>

<stop_conditions>
STOP and ask the user (in addition to `_shared.md § 9` universal stops) if:

- An existing service `appsettings.json` already has conflicting connection-string keys (don't blindly overwrite).
- The Keycloak realm export has a structure that doesn't match expectations from ADR-0010 (older Keycloak version with different schema).
- A NuGet package referenced in `<contract>` doesn't have a stable .NET 10 version available — pivot to closest equivalent and document.
- Existing `.github/workflows/` has a workflow that conflicts with `schema-compat.yml`.
- Adding the Azurite container conflicts with existing Postgres / Kafka / Redis ports on the host.
</stop_conditions>

<session_management>
Per `_shared.md § 10`. Wave-0-specific suggested commit milestones:

1. `Platform.SharedKernel` additions (`Money`, `Address`) + unit tests
2. `Platform.ServiceDefaults` extensions (correlation-id middleware) + integration test
3. `Platform.ServiceDefaults` extensions (service-auth + feature-flags) + integration test
4. `Platform.KafkaFlow.ProducerHeaders` extensions + roundtrip test
5. `docker-compose.yaml` updates (topics + relays + Redis split + Azurite + nginx-cdn) + smoke verify
6. Keycloak realm + scope matrix
7. `flags.json` + per-service `appsettings.json` updates
8. Schema-compat CI gate + first-run validation against main

Adjust to fit; commit after each green test slice.
</session_management>

<verification>
```bash
dotnet build -m
dotnet restore --locked-mode
dotnet format whitespace --no-restore --verify-no-changes
dotnet format style --no-restore --verify-no-changes
dotnet test

# Topology verification
docker compose --profile full config > /tmp/compose.yaml  # validates the file
docker compose --profile full up -d
sleep 30   # let everything start

# Kafka topics — expect 8 new + 2 renamed (payments.*)
docker compose exec kafka kafka-topics --bootstrap-server kafka:9092 --list \
  | grep -E '(catalog|basket|ordering|inventory|invoicing|payments)\.' | sort

# Azurite + invoices container
curl -s http://localhost:10000/devstoreaccount1?comp=list  # should return XML

# nginx-cdn proxies
curl -I http://localhost:8080/  # should return 200/404 from azurite proxied

# Redis split — check policies
docker compose exec redis-basket redis-cli CONFIG GET maxmemory-policy  # expect "noeviction"
docker compose exec redis-cache redis-cli CONFIG GET maxmemory-policy   # expect "allkeys-lru"

# Keycloak realm has new clients
curl -s "http://localhost:8081/realms/eshop/.well-known/openid-configuration" | jq .issuer

# Schema-compat CI gate dry run (manually invoke against main)
gh workflow run schema-compat.yml --ref main
```

Paste actual command output (pass/fail per command) into your session summary.
</verification>

<example_design_decision>
For one of the `<design_open>` items, here's the depth expected:

**Question:** Schema-compat CI gate exact mechanism — shell script vs reusable workflow vs custom GitHub Action?

**Bad answer:** "I'll use a shell script."

**Good answer:** "Bash script in `.github/workflows/schema-compat.yml` running inline. Reasons: (1) the gate logic is ≤ 60 lines (start Schema Registry container, POST `.avsc` files via curl, check HTTP 200), and a custom action would add maintenance overhead for one repo; (2) reusable workflow is overkill for a single-repo gate that has no shared logic; (3) inline shell is debuggable in the Actions log without leaving the workflow. Trade-off accepted: if a second repo ever needs the same gate, we'd extract to a custom action then. Verified by: deliberately broken `.avsc` PR test reproduces a red build."

That's the depth expected for **every** `<design_open>` resolution.
</example_design_decision>

<peer_review>
Per `_shared.md § 11`. Before declaring DoD met:

1. Run every command in `<verification>`; paste pass/fail output in session summary.
2. Invoke `superpowers:verification-before-completion`.
3. Invoke `nw-software-crafter-reviewer` with your session summary as input — fix any HIGH-severity findings.
</peer_review>

<session_summary>
Use the template in `_template.md § session_summary`, plus Wave-0-specifics:

- File-by-file change inventory (~50-file PR is normal; reviewers need a map)
- Aspire AppHost decision: **confirmed deferred** to a later session (do not introduce one)
- OpenFeature .NET package version pinned + why
- Schema-compat CI gate first-run result against `main` (pass / found drift + remediation)
- Any deviations from the ADRs + justification
- BC-agent-readiness checklist: confirm each Wave-1 BC can dispatch with no blocking dependencies on Wave 0 work
- Connection-string conventions and where each is read (per service)
- Production-rotation reminder: dev secrets in Keycloak realm export are committed for reproducibility — production must rotate

Proceed.
</session_summary>
