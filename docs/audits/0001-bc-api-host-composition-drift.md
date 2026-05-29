# Audit 0001 — BC API host-composition drift

**Date:** 2026-05-30
**Prompted by:** #280 (developer index page)
**Status:** Resolved — drift fixed, conventions captured below. No ADR (per maintainer: not weighty enough to warrant one).

## Context

The six HTTP bounded-context (BC) APIs — **Basket, Catalog, Inventory, Invoicing,
Ordering, Payments** — had drifted apart in their `Program.cs` pipeline, observability
primitives, and authorization wiring. A seventh service, **Notifications**, is a Kafka
worker with no HTTP/auth surface and is out of scope except as a comparison point.

The drift tells a **"two cohorts" story**:

| Cohort | BCs | Traits |
|---|---|---|
| **v1 template** | Basket, Catalog, Inventory | Fluent middleware chain, `UseCors`, Prometheus health exporter, bare `ServiceName` |
| **v2 template** | Invoicing, Ordering, Payments | Statement-style pipeline, no CORS, no Prometheus exporter, `".Api"`-suffixed `ServiceName` |

Some divergences are **intentional and documented** (CORS, output cache, outbound
service-auth track BC type). Others were **accidental drift** from the two templates
falling out of sync. This audit classifies each, fixes the accidental drift, and records
the conventions so future BCs stop guessing.

## Findings

| # | Divergence | Verdict | Evidence |
|---|---|---|---|
| F1 | `ApplicationInfo` primitive | **RESOLVED** | All 7 BCs now expose `public static ApplicationInfo` (`AppName` + `Version`). Catalog & Payments — the two that previously inlined private consts — now carry it ([Catalog](../../services/Catalog/Catalog.Infrastructure/Common/Observability/ApplicationInfo.cs), [Payments](../../services/Payments/Payments.Infrastructure/Common/Observability/ApplicationInfo.cs)) and their `ObservabilityDependencyInjection` references `ApplicationInfo.AppName`/`.Version` ([Catalog L40,44](../../services/Catalog/Catalog.Infrastructure/Common/ObservabilityDependencyInjection.cs:40)). Closed by #280. |
| F2 | `UsePlatformHealthChecksPrometheusExporter()` absent in Invoicing/Ordering/Payments | **DRIFT → FIXED** | Cross-cutting observability signal, unrelated to admin/internal status. Now present in all 6: Basket [P.cs:57](../../services/Basket/Basket.Api/Program.cs:57), Invoicing [P.cs:62](../../services/Invoicing/Invoicing.Api/Program.cs:62), Ordering [P.cs:62](../../services/Ordering/Ordering.Api/Program.cs:62), Payments [P.cs:57](../../services/Payments/Payments.Api/Program.cs:57) — wired immediately after `MapPlatformHealthCheckEndpoints()`. |
| F3 | `UseCors` only in Basket/Catalog/Inventory | **INTENTIONAL** | Invoicing/Ordering/Payments `ApiDependencyInjection.cs` each state verbatim *"X is an admin/internal API — no CORS is wired"* ([Payments L8](../../services/Payments/Payments.Api/Common/ApiDependencyInjection.cs:8), [Invoicing L10](../../services/Invoicing/Invoicing.Api/Common/ApiDependencyInjection.cs:10), [Ordering L11](../../services/Ordering/Ordering.Api/Common/ApiDependencyInjection.cs:11)). CORS tracks browser-facing BCs. |
| F4 | `UseOutputCache` absent in Payments | **INTENTIONAL** | [Payments ApiDependencyInjection.cs:7](../../services/Payments/Payments.Api/Common/ApiDependencyInjection.cs:7): *"ADR-0013's idempotency-key output cache is intentionally NOT wired"* — no state-changing HTTP endpoints in v1. Invoicing/Ordering wire it for their idempotent mutations. |
| F5 | Host service-name token inconsistent | **DRIFT → FIXED** | `options.ServiceName` was `"Invoicing.Api"`/`"Ordering.Api"`/`"Payments.Api"` but the OTel resource `service.name` (via `ApplicationInfo.AppName`) and docker-compose `OTEL_SERVICE_NAME` were the bare BC name — logs disagreed with traces. Now bare everywhere ([Invoicing P.cs:22](../../services/Invoicing/Invoicing.Api/Program.cs:22), [Ordering P.cs:22](../../services/Ordering/Ordering.Api/Program.cs:22), [Payments P.cs:22](../../services/Payments/Payments.Api/Program.cs:22)). `ServiceName` feeds Serilog enrichment → OTLP `service.name`. |
| F6 | Fluent-chain vs statement middleware style | **DRIFT → FIXED** | Invoicing/Ordering/Payments used separate `app.Use*()` statements; normalized to the v1 fluent chain off `app.UseRouting()` ([Basket P.cs:46-50](../../services/Basket/Basket.Api/Program.cs:46) reference; [Invoicing P.cs:52-55](../../services/Invoicing/Invoicing.Api/Program.cs:52), [Payments P.cs:48-50](../../services/Payments/Payments.Api/Program.cs:48)). Behavior-identical; the "OutputCache before authn" comment moved above the chain. |
| F7 | Authz policy structure split | **DRIFT → FIXED** | Catalog & Inventory used dedicated `*AuthorizationPolicies` extension classes plus a speculative `["scope","scp"]` multi-IdP predicate. Normalized to the inline pattern Payments already used: `AddAuthorizationBuilder().AddPolicy(...)` in `AuthenticationDependencyInjection.cs`, name/scope/role constants in `AuthPolicies.cs`/`Scopes.cs`/`Roles.cs`, single `scope` claim. Scope semantics preserved exactly. ([Catalog DI L52-62](../../services/Catalog/Catalog.Api/Common/AuthenticationDependencyInjection.cs:52), [Inventory DI L59-70](../../services/Inventory/Inventory.Api/Common/AuthenticationDependencyInjection.cs:59).) |
| F8 | `AddServiceAuth` present only in Basket/Catalog/Ordering | **INTENTIONAL** | Inventory/Invoicing/Payments `AuthenticationDependencyInjection.cs` each document *"v1 has no outbound HTTP calls so AddServiceAuth is intentionally not wired"* ([Inventory L17](../../services/Inventory/Inventory.Api/Common/AuthenticationDependencyInjection.cs:17), [Invoicing L18](../../services/Invoicing/Invoicing.Api/Common/AuthenticationDependencyInjection.cs:18), [Payments L19](../../services/Payments/Payments.Api/Common/AuthenticationDependencyInjection.cs:19)); their appsettings omit the `ServiceAuth` section accordingly. Tracks BCs with outbound BC calls. |

### Authorization semantics preserved (F7 detail)

The refactor was strictly structural. The enforced behavior per BC is unchanged:

| BC | Read policy | Write policy |
|---|---|---|
| **Catalog** | `catalog.read` OR `catalog.write` | `catalog.write` scope (no role) |
| **Inventory** | `inventory.read` OR `inventory.write` | `admin` realm role **AND** `inventory.write` scope (defense-in-depth, mirrors `PaymentsAdmin`) |

> Note: the original plan referenced Inventory scopes as `inventory.commands.reserve` — that
> was a stale snapshot. The live code uses `inventory.read` / `inventory.write` with an `admin`
> role gate on writes, and the refactor preserved exactly that.

## Conventions going forward

So future BCs don't re-derive the template:

**Required in every HTTP BC** — wired as the **fluent chain** off `app.UseRouting()`:
exception handler (prod) / developer page, status-code pages, correlation-id (ADR-0008),
routing, authentication + authorization, FastEndpoints, health-check mapping, **Prometheus
health exporter** (`UsePlatformHealthChecksPrometheusExporter()` right after
`MapPlatformHealthCheckEndpoints()`), dev-only migration on startup.

**Conditional by BC type** — each omission carries a one-line rationale comment in
`ApiDependencyInjection.cs` / `AuthenticationDependencyInjection.cs`:
- **CORS** → browser-facing BCs only (per-BC `CorsOptions` policy). Admin/internal APIs omit it.
- **Idempotency output cache** (`UseOutputCache`, ADR-0013) → BCs with idempotent
  state-changing endpoints. BCs with no mutating HTTP surface omit it.
- **`AddServiceAuth`** → BCs that make outbound HTTP calls to other BCs. BCs with no
  outbound calls omit it (and omit the `ServiceAuth` appsettings section).

**Observability identity** — one token per BC, equal everywhere:
`options.ServiceName` = OTel `service.name` = `ApplicationInfo.AppName` =
docker-compose `OTEL_SERVICE_NAME` = the **bare BC name** (`"Invoicing"`, not `"Invoicing.Api"`).
Every BC exposes `public static ApplicationInfo` (`AppName` + assembly `Version`).

**Authorization wiring** — registered inline in `AuthenticationDependencyInjection.cs` via
`AddAuthorizationBuilder().AddPolicy(...)`; policy-name / scope / role constants live in
`AuthPolicies.cs` / `Scopes.cs` / `Roles.cs`; **no** dedicated policy-helper classes; read the
single space-separated `scope` claim only (Keycloak, RFC 6749); **no** speculative multi-IdP
claim handling. Reads are satisfied by the read **or** write scope; writes that need a human
operator add `RequireRole(Roles.Admin)` on top of the write scope (defense-in-depth).

## Cross-references

- ADR-0008 — correlation-id propagation
- ADR-0010 — service-to-service authentication (scopes, audiences)
- ADR-0013 — idempotency keys / output cache

## Out of scope

Notifications (no HTTP surface), any change to scope **string values** or the Keycloak realm
(unchanged — only class-name references in prose were repointed), and any ADR.
