# ADR-0010: Service-to-Service Authentication via OAuth2 Client Credentials

## Status

Accepted (2026-04-19)

## Context

The eShop reference solution has user authentication (Keycloak-issued JWT, validated at YARP). It does **not** yet have service-to-service authentication: when Checkout saga publishes `ReserveStockCommand` to `inventory.reservation-commands`, the Inventory consumer trusts any message on the topic; when BFF calls Catalog's HTTP API with a forwarded user token, there is no audience restriction that says "only BFF may call me with a service ticket". The gap is documented in threat model § C2 (threat-model + pre-mortem review): topic ACLs are "documented trust, not enforced trust", and HTTP service hops lack audience validation.

At reference-solution scale (single AZ, single trust zone), a strong attacker model is not realistic. Still, the reference should teach the standard industry pattern so readers see what to implement in production. Keycloak is already deployed; leveraging it for service clients is strictly less infrastructure than adding mTLS or SPIFFE.

## Decision Drivers (ranked)

1. **Teach the industry pattern** — OAuth2 Client Credentials is what most real microservices use. Showing it here transfers directly to production.
2. **Reuse existing infra** — Keycloak is already a dependency. No new components.
3. **Laptop-testable** — every developer can run the full flow locally.
4. **Separation of user-auth and service-auth** — user JWTs and service tokens must be distinguishable so services enforce least privilege.
5. **Low friction for new services** — adding service-auth to a new service should be a single DI extension call, not an afterthought.

## Considered Options

### Option 1: OAuth2 Client Credentials via Keycloak

Every service is a Keycloak **client** with a client secret. Outbound HTTP calls go through a `ClientCredentialsTokenHandler` that fetches a service-account token from Keycloak (cached until near expiry). Token carries `azp` (calling service), `aud` (target service), `scope`. Kafka commands carry the same token in a header; inbox consumers validate before dispatching to the handler.

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

We will use **Option 1: OAuth2 Client Credentials grant via Keycloak**, with per-service clients, audience-scoped tokens, and Kafka-header token propagation for command topics.

## Rationale

Option 1 hits every decision driver without adding infrastructure. Keycloak already issues user JWTs; adding per-service clients is a config-file change. The `ClientCredentialsTokenHandler` pattern is well-documented, widely taught, and maps directly to what readers will encounter at AWS (Cognito client credentials), Azure (AAD service principals), or GCP (service account tokens). Option 2 (mesh) is operationally simpler at scale but imposes Istio/Linkerd as a required dependency; that is a production posture, not a learning step. Option 3 (SPIFFE) is where production k8s shops are heading but has a steeper on-ramp. Option 4 forfeits scope + audience enforcement; it's the "shared password" of service auth.

A subtle but important point: Option 1 lets us teach **scopes** explicitly. A token for `catalog-service` with scope `catalog.read` is a clean example of least-privilege that maps 1:1 to how production microservices enforce boundaries.

## Consequences

### Positive

- Same Keycloak, same terminology as user auth — lower cognitive load.
- Tokens carry `azp` (calling service identity) which feeds audit columns (e.g., `admin_audit.actor_service_id`).
- Scopes enforce least privilege: `payments-service` cannot call Ordering's `MarkOrderShippedCommand` because its token lacks the scope.
- Kafka command topics get a validated sender identity: inbox consumer rejects commands whose token scope doesn't authorize the command type.
- New services adopt with `builder.Services.AddServiceAuth("catalog-service").WithScopes("inventory.read")` in `Platform.ServiceDefaults`.
- Tokens are short-lived (≤ 5 min by default); revocation happens naturally via expiry.

### Negative

- Every service now talks to Keycloak on startup and periodically. Adds one more dependency in the critical path. Mitigation: Keycloak outage is tolerated via the token cache (existing valid tokens keep working until expiry).
- Kafka message headers grow by ~500 bytes (JWT). Negligible at reference scale.
- Developers must register new clients in Keycloak realm config for any new service. Mitigation: `keycloak/realm-export.json` is version-controlled; a PR adds the client.

### Risks

- **Client secret leak** — env-based secrets are not rotated in v1. Mitigation: document rotation procedure; production would use dynamic secrets (Vault / cloud KMS).
- **Token validation failure on Kafka consumer** — if Keycloak signing keys rotate, consumers may reject valid tokens briefly. Mitigation: JWKS endpoint polled every 15 minutes; tolerance window of 5 min on clock skew.
- **Over-privilege** — a service requests too broad a scope set. Mitigation: scopes are defined centrally in `keycloak/realm-export.json`; code review.
- **Broker-level auth absent** — v1 does not enable Kafka SASL/OAUTHBEARER, so a rogue process inside the docker network could still produce to any topic. Acknowledged as a reference-solution simplification (ADR-0009 profile). Production must enable broker-level auth.

## Implementation Notes

- **Realm setup** (in `keycloak/realm-export.json`):
  - One client per service: `catalog-service`, `basket-service`, `ordering-service`, `inventory-service`, `payments-service`, `invoicing-service`, `checkout-saga`, `notifications-service`, `bff`.
  - Each client has `serviceAccountsEnabled: true`, `publicClient: false`, client-secret stored as env var `KEYCLOAK__SERVICE_CLIENT_SECRET__<service>`.
  - Scopes defined per target service: `catalog.read`, `catalog.write`, `inventory.commands.reserve`, `inventory.commands.confirm`, `ordering.commands.*`, `payments.commands.*`, etc.
  - Service-to-scope matrix is documented in `keycloak/service-scope-matrix.md` (co-authored with this ADR).

- **Token acquisition flow (outbound HTTP):**
  1. `ClientCredentialsTokenHandler` (in `Platform.ServiceDefaults`, new class) intercepts every outgoing HttpClient request.
  2. Checks in-memory cache keyed `(target-service, scope)` for a valid token.
  3. If absent / expiring within 30s, POST to `{keycloak}/realms/eshop/protocol/openid-connect/token` with `grant_type=client_credentials`, `client_id`, `client_secret`, `scope`, `audience=<target-service>`.
  4. Caches token; attaches `Authorization: Bearer <token>` to the outbound request.
  5. On 401 from target, invalidates cache and retries once (handles signing-key rotation edge).

- **Token validation (inbound HTTP):**
  - Per-service ASP.NET `AddJwtBearer` config: `Authority = {keycloak}/realms/eshop`, `Audience = <this-service>`, `TokenValidationParameters.ValidateIssuer = true`, `ValidateAudience = true`, clock skew 5 min.
  - Scope-based authorization via `RequireClaim("scope", "catalog.read")`-style policies, or FastEndpoints' `Policies(...)` / `Permissions(...)`.

- **Kafka command token propagation:**
  - Outbox publisher writes a Kafka message header `X-Service-Token: Bearer <jwt>` when publishing command topics (not event topics).
  - Inbox consumer middleware (`Platform.KafkaFlow.*`, new middleware class) validates the token before dispatching to the handler. Rejects with `DataIntegrityException` if missing/invalid on a command topic (not event topic — events are fire-and-forget and do not need sender auth).
  - Validation uses the same JWT validation config as HTTP.

- **Scope enforcement per command topic:**
  - `inventory.reservation-commands` consumer requires scope `inventory.commands.reserve` / `inventory.commands.confirm` / `inventory.commands.release` (matching the command type).
  - `payments.commands` consumer requires scope `payments.commands.authorize` / `payments.commands.capture` / etc.
  - `ordering.order-commands` consumer requires scope `ordering.commands.create` / `ordering.commands.confirm` / `ordering.commands.cancel` / `ordering.commands.fail`.
  - Event topics do not require sender auth — consumers are fire-and-forget observers.

- **Observability:**
  - Every validated inbound token adds `auth.client.id = <azp>` to the Activity span.
  - Metric `auth.token.fetch.count` tagged `outcome=cache-hit|miss|failure`.
  - Audit columns in saga state and admin-audit tables capture `actor_service_id` from `azp`.

## Related Decisions

- [ADR-0008: Correlation-ID Propagation Rule](0008-correlation-id-propagation.md) — service-auth identity is separate from CorrelationId; both travel on the same hops
- [ADR-0009: Reference-Solution Target Profile](0009-reference-solution-target-profile.md) — production callouts include "enable Kafka SASL/OAUTHBEARER" (v1 absent)
- [ADR-0004: Checkout Saga Topology](0004-checkout-saga-topology.md) — saga's outbound commands are the primary consumer of this mechanism
- [ADR-0007: Avro Schema Compatibility Modes](0007-avro-compatibility-modes.md) — service-auth token is in Kafka headers, not Avro payload
