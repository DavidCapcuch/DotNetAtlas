# ADR-0010: Service-to-Service Authentication via OAuth2 Client Credentials

## Status

Accepted (2026-04-19) · **Amended 2026-05-27** — see [Amendment: Fail-closed audience contract](#amendment-2026-05-27--fail-closed-audience-contract).

## Context

The eShop reference solution has user authentication (Keycloak-issued JWT, validated at YARP). It does **not** yet have service-to-service authentication on its cross-BC HTTP surface: when BFF calls Catalog's HTTP API with a forwarded user token, there is no audience restriction that says "only BFF may call me with a service ticket". The gap is documented in threat model § C2 (threat-model + pre-mortem review): HTTP service hops lack audience validation.

Kafka command topics are explicitly out of scope for application-layer auth in this reference solution — the trust boundary is the docker network per [ADR-0009](0009-reference-solution-target-profile.md). At reference-solution scale (single AZ, single trust zone), a strong attacker model is not realistic. Still, the reference should teach the standard industry pattern for HTTP service-to-service auth so readers see what to implement in production. Keycloak is already deployed; leveraging it for service clients is strictly less infrastructure than adding mTLS or SPIFFE.

## Decision Drivers (ranked)

1. **Teach the industry pattern** — OAuth2 Client Credentials is what most real microservices use. Showing it here transfers directly to production.
2. **Reuse existing infra** — Keycloak is already a dependency. No new components.
3. **Laptop-testable** — every developer can run the full flow locally.
4. **Separation of user-auth and service-auth** — user JWTs and service tokens must be distinguishable so services enforce least privilege.
5. **Low friction for new services** — adding service-auth to a new service should be a single DI extension call, not an afterthought.

## Considered Options

### Option 1: OAuth2 Client Credentials via Keycloak

Every service is a Keycloak **client** with a client secret. Outbound HTTP calls go through a `ClientCredentialsTokenHandler` that fetches a service-account token from Keycloak (cached until near expiry). Token carries `azp` (calling service), `aud` (target service), `scope`. Inbound HTTP endpoints validate the token via `AddJwtBearer` and gate behaviour on the `scope` claim. Kafka command topics carry no application-layer auth — the trust boundary is the docker network per ADR-0009.

### Option 2: mTLS via service mesh (Istio / Linkerd)

Transparent mesh-level TLS with service identity derived from certificates. Kubernetes-native; no application code involvement.

### Option 3: SPIFFE / SPIRE

Cloud-native workload identity framework; service identity is a SPIFFE ID; tokens are SVIDs. More future-proof than mTLS alone.

### Option 4: Shared secret / API key per service-pair

A lightweight bearer token minted once per service-pair and stored in env. No Keycloak involvement.

## Evaluation Matrix

| Driver (ranked) | Option 1: Client Credentials | Option 2: mTLS / Mesh | Option 3: SPIFFE | Option 4: Shared secret |
|---|---|---|---|---|
| 1. Industry pattern | Most common real-world | Common in k8s shops | Less common; emerging | Not industry best practice |
| 2. Reuse existing infra | Keycloak already there | New: service mesh + ca + sidecars | New: SPIRE server + agents | Trivial; no infra |
| 3. Laptop-testable | Works with `docker-compose` | Istio in compose is painful | SPIRE local setup complex | Trivial |
| 4. User vs service separation | Clear: different realm clients, different scopes | mTLS is coarser; identity is hostname-bound | Cleanest separation | No native separation |
| 5. Low friction | One DI extension method | Sidecar injection, network policies | SVID fetch libraries | Copy-paste the secret everywhere |

## Decision

We will use **Option 1: OAuth2 Client Credentials grant via Keycloak**, with per-service clients and audience-scoped tokens, applied to cross-BC HTTP traffic only.

## Rationale

Option 1 hits every decision driver without adding infrastructure. Keycloak already issues user JWTs; adding per-service clients is a config-file change. The `ClientCredentialsTokenHandler` pattern is well-documented, widely taught, and maps directly to what readers will encounter at AWS (Cognito client credentials), Azure (AAD service principals), or GCP (service account tokens). Option 2 (mesh) is operationally simpler at scale but imposes Istio/Linkerd as a required dependency; that is a production posture, not a learning step. Option 3 (SPIFFE) is where production k8s shops are heading but has a steeper on-ramp. Option 4 forfeits scope + audience enforcement; it's the "shared password" of service auth.

A subtle but important point: Option 1 lets us teach **scopes** explicitly. A token for `catalog-service` with scope `catalog.read` is a clean example of least-privilege that maps 1:1 to how production microservices enforce boundaries.

## Consequences

### Positive

- Same Keycloak, same terminology as user auth — lower cognitive load.
- Tokens carry `azp` (calling service identity) which feeds audit columns (e.g., `admin_audit.actor_service_id`).
- Scopes enforce least privilege on HTTP: a caller lacking the required scope is rejected by the BC's `AddJwtBearer` policy (e.g. only callers with `inventory.commands.reserve` can hit the Inventory `Receive`/`Adjust` admin endpoints).
- New services adopt with `builder.Services.AddServiceAuth("catalog-service").WithScopes("inventory.read")` in `Platform.ServiceDefaults`.
- Tokens are short-lived (≤ 5 min by default); revocation happens naturally via expiry.

### Negative

- Every service now talks to Keycloak on startup and periodically. Adds one more dependency in the critical path. Mitigation: Keycloak outage is tolerated via the token cache (existing valid tokens keep working until expiry).
- Developers must register new clients in Keycloak realm config for any new service. Mitigation: `keycloak/realm-export.json` is version-controlled; a PR adds the client.

### Risks

- **Client secret leak** — env-based secrets are not rotated in v1. Mitigation: document rotation procedure; production would use dynamic secrets (Vault / cloud KMS).
- **Token validation failure on signing-key rotation** — if Keycloak signing keys rotate, BC `AddJwtBearer` validators may reject valid tokens briefly. Mitigation: JWKS endpoint polled every 15 minutes; tolerance window of 5 min on clock skew.
- **Over-privilege** — a service requests too broad a scope set. Mitigation: scopes are defined centrally in `keycloak/realm-export.json`; code review.

## Implementation Notes

- **Realm setup** (in `keycloak/realm-export.json`):
  - One client per service: `catalog-service`, `basket-service`, `ordering-service`, `inventory-service`, `payments-service`, `invoicing-service`, `checkout-saga`, `notifications-service`, `bff`.
  - Each client has `serviceAccountsEnabled: true`, `publicClient: false`, client-secret stored as env var `KEYCLOAK__SERVICE_CLIENT_SECRET__<service>`.
  - Scopes defined per target service: `catalog.read`, `catalog.write`, `inventory.read`, `inventory.commands.reserve`, `notifications.commands.send`, etc.
  - Service-to-scope matrix is documented in `keycloak/service-scope-matrix.md` (co-authored with this ADR).

- **Token acquisition flow (outbound HTTP):**
  1. `ClientCredentialsTokenHandler` (in `Platform.ServiceDefaults`, new class) intercepts every outgoing HttpClient request.
  2. Checks in-memory cache keyed `(target-service, scope)` for a valid token.
  3. If absent / expiring within 30s, POST to `{keycloak}/realms/dotnetatlas/protocol/openid-connect/token` with `grant_type=client_credentials`, `client_id`, `client_secret`, `scope`, `audience=<target-service>`.
  4. Caches token; attaches `Authorization: Bearer <token>` to the outbound request.
  5. On 401 from target, invalidates cache and retries once (handles signing-key rotation edge).

- **Token validation (inbound HTTP):**
  - Per-service ASP.NET `AddJwtBearer` config: `Authority = {keycloak}/realms/dotnetatlas`, `Audience = <this-service>`, `TokenValidationParameters.ValidateIssuer = true`, `ValidateAudience = true`, clock skew 5 min.
  - **(Amended 2026-05-27)** `ValidAudience` is no longer derived implicitly from `ServiceAuthOptions.ServiceName`. Each BC pins it explicitly under `Authentication:JwtBearer:TokenValidationParameters:ValidAudience` in `appsettings.json`. See [the amendment below](#amendment-2026-05-27--fail-closed-audience-contract).
  - Scope-based authorization via `RequireClaim("scope", "catalog.read")`-style policies, or FastEndpoints' `Policies(...)` / `Permissions(...)`.

- **Scope enforcement on inbound HTTP (where it does belong):**
  - The HTTP-side `AddJwtBearer` validation above already enforces audience + issuer per service. Scope policies (`RequireClaim("scope", "inventory.commands.reserve")`-style) gate admin endpoints inside each service. This is the only layer where service-to-service tokens need application-level inspection, because admin commands enter via HTTP, not Kafka.
  - **Platform helper (Wave 1.5 cross-cutting):** `Platform.ServiceDefaults.Auth.ScopePolicyExtensions.RequireScope(string scope)` is the canonical way for a BC `AuthorizationPolicyBuilder` to enforce a scope claim. It composes `RequireAuthenticatedUser()` + `RequireClaim("scope", scope)` and validates input. Existing v1 BCs (Invoicing, Payments, Ordering) currently use `RequireRole(Roles.<Admin|Buyer>)` mapped from Keycloak realm roles — a transitional posture flagged in the Wave 1 closeouts. The v2 hardening pass migrates each admin/buyer policy to `RequireScope("<service>.<verb>")` using this helper; per-BC migration is tracked on the issue tracker under label `platform/wave1-followup`.

- **Observability:**
  - Every validated inbound token adds `auth.client.id = <azp>` to the Activity span.
  - Metric `auth.token.fetch.count` tagged `outcome=cache-hit|miss|failure`.
  - Audit columns in saga state and admin-audit tables capture `actor_service_id` from `azp`.

## Related Decisions

- [ADR-0008: Correlation-ID Propagation Rule](0008-correlation-id-propagation.md) — service-auth identity is separate from CorrelationId; both travel on the same hops
- [ADR-0009: Reference-Solution Target Profile](0009-reference-solution-target-profile.md) — single-trust-zone runtime profile defines the auth envelope this ADR fits into
- [ADR-0004: Checkout Saga Topology](0004-checkout-saga-topology.md) — saga's outbound HTTP calls are the primary consumer of this mechanism
- [ADR-0007: Avro Schema Compatibility Modes](0007-avro-compatibility-modes.md) — Avro payloads carry no auth metadata; HTTP is the only application layer that inspects service-auth tokens

## Amendment 2026-05-27 — Fail-closed audience contract

### Context

The original implementation (above) had `JwtBearerConfigurator.AddPlatformJwtBearer` default `TokenValidationParameters.ValidAudience` to `ServiceAuthOptions.ServiceName`. This implicit fallback collapsed two distinct concerns — *what audience does this BC validate inbound tokens against* and *what is this BC's own service identity for outbound `client_credentials` requests* — into one option binding, with two failure modes that motivated this amendment:

1. **Surface vs. deep signal confusion.** A `ServiceAuth` section in `appsettings.json` looked like the BC was wired for outbound auth, but `ServiceAuthOptions` is only bound by an explicit `services.AddServiceAuth(...)` DI call. Inbound-only BCs that had a `ServiceAuth` section but no `AddServiceAuth` call (Inventory, Invoicing, Payments) silently resolved `ServiceAuthOptions.ServiceName` to `""`, the implicit `ValidAudience` fallback then matched nothing, and real Keycloak tokens were rejected — masked from FunctionalTests by a now-removed test-framework override (`JwtBearerTestExtensions` used to overwrite `ValidAudience` with the test signer's audience).
2. **Drift between two strings that had to stay equal.** When a BC was outbound-active, the same value appeared in `ServiceAuth.ServiceName` and (via the fallback) `JwtBearer.ValidAudience`. Refactoring either in isolation broke the other silently.

### Decision (amendment)

Remove the implicit default and fail closed instead. Every BC must explicitly pin `Authentication.JwtBearer.TokenValidationParameters.ValidAudience` in `appsettings.json`. If a BC omits the key, `ValidateAudience=true` + `ValidAudience=null` rejects every token at runtime — loudly, at first auth resolution, with a clear error.

This collapses BCs into two well-defined shapes:

| Shape | Calls `services.AddServiceAuth("<bc>-service")`? | `ServiceAuth` section in `appsettings.json`? | `Authentication.JwtBearer.TokenValidationParameters.ValidAudience` |
|---|---|---|---|
| **Outbound-active** (e.g. Basket, Catalog, Ordering) | Yes | Yes — `ServiceName` must equal `ValidAudience` | Required — set to `"<bc>-service"` |
| **Inbound-only** (e.g. Inventory, Invoicing, Payments) | No | **No — section must be omitted entirely** | Required — set to `"<bc>-service"` |

Creating a `ServiceAuth` section without a matching `AddServiceAuth(...)` call is disallowed: the section is inert without the DI call, and any "pre-provisioned for future outbound calls" config introduces drift risk. Add the section the day an outbound HTTP client lands.

### Defense-in-depth: three-phase options pipeline

`Platform.ServiceDefaults.Auth.JwtBearerConfigurator.AddPlatformJwtBearer` keeps a three-phase pipeline so the security floor stays immutable even if a BC's `configuration.Bind` is misconfigured:

1. **Configure** seeds `JwtBearerOptions` defaults from `ServiceAuthOptions` (Authority, `ValidIssuer = Authority`, the five validation booleans `true`, ClockSkew). `ValidAudience` is intentionally left at its `TokenValidationParameters` default (`null`) so the BC's appsettings binding is the sole source of truth.
2. **BC's configure delegate** runs inside Configure — typically `configuration.Bind("Authentication:JwtBearer", options)` — and is where `ValidAudience` arrives. If a BC forgets the appsettings pin, this step doesn't set it and the runtime rejects every token (fail-closed).
3. **PostConfigure** re-pins the five security booleans (`ValidateIssuer / ValidateAudience / ValidateLifetime / ValidateIssuerSigningKey / RequireSignedTokens`) to `true` after the BC's bind — the immutable security floor. No appsettings, env var, or BC-specific override can silently relax validation (per #223).

Net: the **strings** (`ValidAudience`, `ValidIssuer`) are configurable per BC; the **booleans** are not.

### Keycloak `audience-self` mappers are load-bearing

Each BC client in `keycloak/realm-export.json` carries an `oidc-audience-mapper` named `audience-self` that emits `aud: "<clientId>"`. **Do not delete them as boilerplate.** Empirically verified against Keycloak 26.3 on 2026-05-27: Keycloak's `client_credentials` grant by default does NOT include the requesting client's own ID in the `aud` claim. Without the mapper, tokens are issued with **no `aud` claim at all**, and every BC's `ValidateAudience=true` validator rejects them silently. A warning comment sits above the realm-export mount in `docker-compose.yaml`.

### Test framework no longer masks misconfiguration

`Platform.Test.Framework.Auth.JwtBearerTestExtensions.ConfigureJwtBearerForTests` previously overrode `TokenValidationParameters.ValidAudience = signer.Audience` in a later PostConfigure, masking any BC misconfiguration during FunctionalTests. The override is removed and replaced with an assertion: the BC's effective `ValidAudience` (after the BC's own Configure/PostConfigure chain) must equal the `FakeTokenSigner.Audience` the fixture constructed with. Each fixture now passes the **production** audience (no more decoupled `-tests`-suffixed values). Drift between the two surfaces as a clear `InvalidOperationException` at first auth resolution, naming both values and the appsettings key to fix.

This amendment caught Inventory's silent breakage during its own implementation: tests went red, message was clear, fix was a 5-line appsettings edit.

### Consequences

- Audit a BC's category in 30 seconds: grep its `AuthenticationDependencyInjection.cs` for `services.AddServiceAuth(serviceName:`. Yes → outbound-active. No → inbound-only.
- A BC that exposes inbound HTTP without setting `ValidAudience` cannot accept any token. The platform layer no longer hides the misconfiguration behind a fallback.
- The `service-scope-matrix.md` companion document still lists every service's outbound scopes; what it does NOT do is imply that every entry corresponds to a wired `AddServiceAuth(...)` call. Wire it explicitly per service.

### Out of scope (deferred)

- Worker BCs (`Notifications`, `OutboxRelay`, `SagaOrchestrators`) have no inbound HTTP audience to validate today. `SagaOrchestrators` has a seeded `ServiceAuth` section pre-provisioned for the deferred outbound-auth wiring; per the rule above this section should be removed and re-added together with the `AddServiceAuth(...)` call when the outbound path lands.
