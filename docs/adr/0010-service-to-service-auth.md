# ADR-0010: Service-to-Service Authentication via OAuth2 Client Credentials

## Status

Accepted (2026-04-19) · **Amended 2026-05-27** — see [Amendment: Fail-closed audience contract](#amendment-2026-05-27--fail-closed-audience-contract) · **Amended 2026-06-06** — see [Amendment: BFF token exchange for buyer-scoped callees](#amendment-2026-06-06--bff-token-exchange-for-buyer-scoped-callees).

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

Every HTTP-callable service is a Keycloak **client**; the ones that make outbound calls hold a client secret for the client-credentials grant. Outbound HTTP calls go through a `ClientCredentialsTokenHandler` that fetches a service-account token from Keycloak (cached until near expiry). Token carries `azp` (calling service), `aud` (target service), `scope`. Inbound HTTP endpoints validate the token via `AddJwtBearer` and gate behaviour on the `scope` claim. Kafka command topics carry no application-layer auth — the trust boundary is the docker network per ADR-0009.

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
- Scopes enforce least privilege on HTTP: a caller lacking the required scope is rejected by the BC's `AddJwtBearer` policy (e.g. only callers with the `admin` role AND the `inventory.write` scope can hit the Inventory `Receive`/`Adjust` admin endpoints).
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
  - One client per service reachable over authenticated HTTP: `catalog-service`, `basket-service`, `ordering-service`, `inventory-service`, `payments-service`, `invoicing-service`, `bff`. Two BCs are deliberately absent from that list: the Checkout saga, which exposes only unauthenticated health probes and otherwise interacts over Kafka (which carries no service token); and `Notifications`, whose bell hub does validate `notifications-service` but which nothing calls service-to-service — an audience value with no client and no scope behind it ([service-scope-matrix.md](../../src/keycloak/service-scope-matrix.md) § `notifications`).
  - All are `publicClient: false`. The four outbound-active clients (`catalog-service`, `basket-service`, `ordering-service`, `bff`) set `serviceAccountsEnabled: true` with a client secret stored as env var `KEYCLOAK__SERVICE_CLIENT_SECRET__<service>`; the three inbound-only clients (`inventory-service`, `payments-service`, `invoicing-service`) are `serviceAccountsEnabled: false` with no secret — their `aud: <bc>-service` is stamped by the resource client-scope's `oidc-audience-mapper`, not a service account (see the 2026-05-27 amendment's outbound-active vs inbound-only table).
  - Scopes defined per target service: `catalog.read`, `catalog.write`, `inventory.read`, `inventory.write`, etc.
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
  - Scope-based authorization via the `RequireAnyScope(...)` policy helper (below), surfaced to endpoints through FastEndpoints' `Policies(...)`.

- **Scope enforcement on inbound HTTP (where it does belong):**
  - The HTTP-side `AddJwtBearer` validation above already enforces audience + issuer per service. Scope policies (via `RequireAnyScope(...)`) — often combined with a `RequireRole("admin")` for human-admin endpoints (Payments and Inventory's write gate use **role AND scope**, defense-in-depth) — guard admin endpoints inside each service. This is the only layer where service-to-service tokens need application-level inspection, because admin commands enter via HTTP, not Kafka.
  - **Platform helper:** `Platform.ServiceDefaults.Auth.ScopePolicyExtensions.RequireAnyScope(params string[] scopes)` is the canonical way for a BC `AuthorizationPolicyBuilder` to enforce scopes. It composes `RequireAuthenticatedUser()` + an assertion that splits the space-separated `scope` claim (RFC 6749; also handles one-claim-per-scope IdPs) and matches **any** of the supplied scopes — covering the read-or-write hierarchy (a write-scoped token also satisfies the read policy). Catalog, Inventory, and Payments register their scope policies through it; human-admin write gates (Inventory, Payments) stack `RequireRole(Roles.Admin)` on top for defense in depth. Ordering's admin endpoints are role-only (no per-verb scope) by design.

- **Observability:**
  - Every validated inbound token adds `auth.client.id = <azp>` to the Activity span.
  - Metric `auth.token.fetch.count` tagged `outcome=cache-hit|miss|failure`.
  - Audit columns in saga state and admin-audit tables capture `actor_service_id` from `azp`.

## Related Decisions

- [ADR-0009: Reference-Solution Target Profile](0009-reference-solution-target-profile.md) — single-trust-zone runtime profile defines the auth envelope this ADR fits into
- [ADR-0004: Checkout Saga Topology](0004-checkout-saga-topology.md) — the saga coordinates BCs via Kafka commands (no service token); the HTTP consumers of this mechanism are the BFF and inter-BC ACL calls (e.g. Basket→Catalog)
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

### Keycloak audience lives on the client SCOPE, not the caller (corrected 2026-05-27)

**Principle (RFC 9068 / RFC 8707):** an access token's `aud` claim identifies the **resource being called** (the callee), and each resource server validates that its own name is in `aud`. The audience is therefore a property of *what you're accessing*, not *who you are*.

**Implementation:** each resource **client scope** in `keycloak/realm-export.json` carries an `oidc-audience-mapper` stamping the owning service as the audience:

| Scope | `aud` added |
|---|---|
| `catalog.read`, `catalog.write` | `catalog-service` |
| `basket.read`, `basket.write` | `basket-service` |
| `ordering.read` | `ordering-service` |
| `inventory.read`, `inventory.write` | `inventory-service` |
| `payments.read` | `payments-service` |
| `invoicing.read` | `invoicing-service` |

So a caller requesting `catalog.read` gets `aud: catalog-service` regardless of which client it is — exactly what catalog-service validates. Requesting multiple scopes yields a multi-valued `aud` array (one entry per resource). A token with no resource scope carries no `aud` and is valid at no resource (fail-closed).

**Service clients carry NO per-client audience mapper.** The earlier design stamped every client with an `audience-self` mapper emitting `aud: "<own-clientId>"`. That was wrong for callers: a token basket sent to catalog carried `aud: basket-service`, which catalog-service rejects. Removing `audience-self` and moving audience to the scope fixes the latent cross-service breakage (verified 2026-05-27: `basket-service` + `catalog.read` → `aud: catalog-service`). The only client that self-audiences is the user-facing app client (the SPA / web client the user signs into — distinct from the `bff` service-account client, which carries outbound BC scopes but no self-audience mapper) — there the app *is* the resource the user token targets. No client in the realm self-audiences today:

**Do not delete the scope-level audience mappers, and do not re-add per-client `audience-self`.** Keycloak's `client_credentials` grant emits no `aud` by default; without the scope mapper a token reaches a resource with no audience and `ValidateAudience=true` rejects it silently. A warning comment sits above the realm-export mount in `docker-compose.yaml`.

### Test framework no longer masks misconfiguration

`Platform.Test.Framework.Auth.JwtBearerTestExtensions.ConfigureJwtBearerForTests` previously overrode `TokenValidationParameters.ValidAudience = signer.Audience` in a later PostConfigure, masking any BC misconfiguration during FunctionalTests. The override is removed and replaced with an assertion: the BC's effective `ValidAudience` (after the BC's own Configure/PostConfigure chain) must equal the `FakeTokenSigner.Audience` the fixture constructed with. Each fixture now passes the **production** audience (no more decoupled `-tests`-suffixed values). Drift between the two surfaces as a clear `InvalidOperationException` at first auth resolution, naming both values and the appsettings key to fix.

This amendment caught Inventory's silent breakage during its own implementation: tests went red, message was clear, fix was a 5-line appsettings edit.

### Consequences

- Audit a BC's category in 30 seconds: grep its `AuthenticationDependencyInjection.cs` for `services.AddServiceAuth(serviceName:`. Yes → outbound-active. No → inbound-only.
- A BC that exposes inbound HTTP without setting `ValidAudience` cannot accept any token. The platform layer no longer hides the misconfiguration behind a fallback.
- The `service-scope-matrix.md` companion document still lists every service's outbound scopes; what it does NOT do is imply that every entry corresponds to a wired `AddServiceAuth(...)` call. Wire it explicitly per service.
- Neither shape covers a BC whose only HTTP surface is the unauthenticated health/metrics endpoints: `OutboxRelay` and `SagaOrchestrators` carry all business traffic over Kafka, so there is no inbound `ValidAudience` to pin and no outbound `ServiceAuth` section to add.

## Amendment 2026-05-30 — Role vs scope canonical model

### Context

The Implementation Notes above named **role AND scope** as the defense-in-depth gate for
human-admin endpoints (Payments, Inventory) and flagged Ordering/Invoicing's `RequireRole`-only
posture as a transitional state to be migrated to scopes "in v2". In practice the gates had
drifted into three different shapes with two reachability holes:

- **Catalog** gated admin writes on the `catalog.write` scope **only** (no role) — a leaked
  service token could mutate the catalog with no human-admin identity.
- **Payments** required `admin` role AND `payments.read`, but `payments.read` was granted to no
  client, so the admin endpoints were unreachable through real Keycloak (closed except via test
  fakes).
- The `dotnetatlas-swagger` client — the human-admin entry point — held only
  `[offline_access, inventory.write]`, so a human admin could not obtain `catalog.write` or
  `payments.read` to exercise those admin endpoints.

### Decision (amendment)

Codify a single rule and stop treating "migrate everything to scopes" as the end state:

- **Roles = *who the human is*** (admin RBAC). **Scopes = *what a service may do*** (service-to-
  service delegation; the scope also stamps the callee audience per the 2026-05-27 amendment).
- **Use role + scope (defense-in-depth) only where a real service-delegation scope exists** for
  that BC's write surface. The scope half adds value precisely because it both proves explicit
  write-capability elevation *and* carries the resource audience.
- **Pure human-admin endpoints with no service-delegation dimension stay role-only.** Ordering's
  ship/deliver and Invoicing's resend have no service caller (their state changes arrive over
  Kafka), so inventing `ordering.write` / `invoicing.write` scopes that only the swagger client
  would ever request would be "provisioned-for-someday" dead config. **This supersedes the
  Implementation-Notes line that planned a v2 scope migration for Ordering/Invoicing** — role-only
  *is* the canonical shape for pure human-admin endpoints.
- **Reads stay scope-only.** BFF-consumed reads are service delegation; we do **not** add an
  `admin` role branch to read policies. Because the write scope implies read (`RequireAnyScope`
  lists both), an admin holding the write scope still satisfies the read policy without a role
  branch.
- **Human admins obtain scopes through the `dotnetatlas-swagger` client** (`optionalClientScopes`).
  The role gate still blocks non-admins, so granting an admin scope to the swagger client does not
  weaken the gate — a non-admin requesting the scope still fails the role check.

### Canonical per-BC gate table

| BC | Admin write/mutating endpoints | Gate | Reads |
|---|---|---|---|
| Catalog | CreateProduct, UpdateProductPrice, Discontinue, Reactivate, CreateCategory, ReparentCategory, SearchAdminProducts, DescribeProduct | **role + `catalog.write`** | `catalog.read`\|`catalog.write` (scope) |
| Inventory | Receive, Adjust | role + `inventory.write` | `inventory.read`\|`inventory.write` (scope) |
| Payments | GetPaymentById, GetPaymentsByOrder (human-admin reads) | role + `payments.read` | — (only the admin reads) |
| Ordering | MarkOrderShipped, MarkOrderDelivered | **role-only** | handler-enforced (buyer-self OR admin) |
| Invoicing | ResendInvoice | **role-only** | handler-enforced (buyer-self OR admin) |
| Basket | — (no admin surface) | authenticated user (self) | self |

### How human admins obtain admin scopes

The `dotnetatlas-swagger` client's `optionalClientScopes` is the canonical source of admin scopes
for a human operator: `[offline_access, inventory.write, catalog.write, payments.read]`. A human
admin authenticates through Swagger (authorization-code + PKCE), requests the scope(s) for the
endpoint(s) they need, and the realm stamps the matching resource audience on the token. Only the
admin scopes are provisioned — read scopes (`catalog.read`, `inventory.read`, …) are **not** added,
because those are service-delegation scopes the BFF carries, and an admin holding the write scope
already satisfies the corresponding read policy.

### Consequences

- One mental model: grep a BC's `AuthenticationDependencyInjection.cs` — `RequireRole` alone →
  pure human-admin; `RequireRole` + `RequireAnyScope` → human-admin with a service-delegation
  dimension; `RequireAnyScope` alone → service-to-service read.
- Every human-admin endpoint is reachable by an `admin` user through the swagger client, and every
  role gate is pinned by a negative test (correct scope / authenticated non-admin → 403).
- The `service-scope-matrix.md` companion documents the swagger client's admin-scope provisioning
  and Payments' human-admin reachability alongside the seven per-BC clients (four service-account, three inbound-only).

## Amendment 2026-06-06 — BFF token exchange for buyer-scoped callees

### Context

The Implementation Notes describe one outbound mechanism — `ClientCredentialsTokenHandler`
attaching a `client_credentials` service token to every outbound HTTP call — and the BFF was
expected to "forward the user JWT alongside" for buyer-scoped requests ("both tokens travel
together"). That mental model does not survive contact with the code:

- `ClientCredentialsTokenHandler.SendAsync` **overwrites** `Authorization` with the service token;
  there is no second-header / on-behalf-of channel, so exactly one token reaches the callee.
- Three BFF-fronted BCs derive the **resource owner from the token `sub`** and enforce buyer-self:
  Basket (`GetUserIdFromSubClaim`), Ordering and Invoicing (`User.GetBuyerIdOrNull()`). A
  `client_credentials` token's `sub` is the BFF **service-account**, so it resolves the wrong buyer
  — and does so **silently**: the audience passes, the wrong `sub` is simply read.
- Those three BCs enforce **no scope policy** on the buyer-scoped routes; the `basket.read` /
  `basket.write` / `ordering.read` / `invoicing.read` scopes exist only to stamp the callee
  **audience** (2026-05-27 amendment). So nothing rejects a service token there — it authenticates
  and mis-resolves the owner.

Surfaced by [#323](https://github.com/DavidCapcuch/DotNetAtlas/issues/323) (whether the `bff`
client needs a Basket scope); the answer generalizes to every buyer-scoped callee.

### Decision (amendment)

The BFF uses **two** outbound shapes, selected by whether the callee owner-scopes on `sub`:

| Callee + routes | Owner from `sub`? | BFF token | Scope's role |
|---|---|---|---|
| Catalog reads; Inventory reads | No (scope-policy-gated) | `client_credentials` service token (`AddServiceAuth`) | gate **and** audience |
| Basket `GET /basket` + `POST /checkout`; Ordering order reads; Invoicing invoice reads | Yes (buyer-self) | **RFC 8693 token exchange** of the user JWT, preserving `sub` | audience only |

The exchange re-audiences the user's token to the callee via the requested scope's
`oidc-audience-mapper` (2026-05-27 amendment) while keeping the user `sub`, so the callee's
`ValidateAudience` passes **and** its `sub`-based owner resolution stays correct. Plain
`client_credentials` is reserved for callees that do not owner-scope.

### Implementation — Keycloak Standard Token Exchange v2 (landed #329)

The exchange is Keycloak's **Standard Token Exchange** (RFC 8693), GA since Keycloak 26.2 and
default-on in the pinned **26.3.2** (feature `token-exchange-standard`) — **not** the legacy
`token-exchange` preview feature, and with **no** fine-grained admin permissions (FGAP). An earlier
draft of this amendment said "turn on the `token-exchange` feature + grant the `bff` client exchange
permission per callee"; that describes the legacy v1 model and does **not** apply to 26.3.2. The
standard model needs only:

- **Requester opt-in:** the `bff` client sets `standard.token.exchange.enabled = "true"`
  (`realm-export.json`). No per-callee permission objects.
- **Holder constraint:** v2 only exchanges a `subject_token` that carries the requester (`bff`) in
  its `aud`. The user-facing client therefore stamps `aud: bff` (the `audience-bff`
  `oidc-audience-mapper` on `dotnetatlas-swagger`; a real SPA client inherits the same obligation).
  The BFF also validates this audience inbound (`ValidAudience = bff`).
- **Callee audience rides the scope:** the exchange requests `scope=<basket.read|…>`, whose
  `oidc-audience-mapper` re-audiences the exchanged token to the callee while the user `sub` is
  preserved (so Basket's `GetUserIdFromSubClaim` / Ordering-Invoicing buyer-self still resolve the
  buyer).

`TokenExchangeHandler` (`Platform.ServiceDefaults.Auth`) performs the exchange, caches per
(`sub`, scope) — never serving one user's token to another — and 401-invalidates-retries-once. One
isolated Keycloak-Testcontainer test proves the end-to-end path (an exchanged `basket.read` token is
accepted by Basket's real JwtBearer validator and resolves the correct buyer), retiring the prior
caveat that the Basket / Ordering / Invoicing FunctionalTests' synthetic buyer-`sub` + callee-`aud`
token was not end-to-end proof.

### Consequences

- The "both tokens travel together" framing in the Implementation Notes is **superseded** for
  buyer-scoped callees: one exchanged token carries both the buyer `sub` and the callee audience.
- Decision rule for a future callee: if the BC resolves the owner from `sub` (buyer-self), the BFF
  must token-exchange; if it only scope-gates a non-owner-scoped read, a plain service token is
  correct.
- Consumer access to buyer-scoped BCs is **BFF-mediated** — there is no direct consumer→BC path.
  So the user-facing app client provisions **no** BC scope and a user JWT never carries
  a BC audience; the BFF's exchanged token is the only token those BCs accept. (This is why the
  scope question raised by [#323](https://github.com/DavidCapcuch/DotNetAtlas/issues/323) does not
  recur on the app client: the gap is closed by routing through the BFF, not by widening the user
  token's audience.) The BFF therefore fronts the full basket surface — read, item mutations, and
  checkout — see [bff.md § 2.5 / § 3.6 / § 4.2](../bc-design/bff.md).
- BFF endpoint spec: [bff.md § 2.3 / § 3.5](../bc-design/bff.md).

## Amendment 2026-07-19 — Deployed-environment HTTPS guard on the outbound Authority

`ServiceAuthOptions.Authority` feeds the Keycloak token endpoint that receives this service's
client-credentials `client_secret` and its RFC 8693 exchanged user tokens. A plaintext `http://`
Authority in a deployed host POSTs those secrets in the clear — the outbound mirror of the inbound
`RequireHttpsMetadata` MITM surface.

`AddServiceAuth` therefore fails closed: in a deployed environment
(`IHostEnvironment.IsDeployedEnvironment()`) a non-`https://` `Authority` fails options validation on
the existing `ValidateOnStart` chain, so the host **refuses to boot**. No-op in Development / Testing
(local http Keycloak). The guard lives once in the platform `AddServiceAuth`, covering both the
`ClientCredentialsTokenHandler` and `TokenExchangeHandler` paths (both bind the same
`ServiceAuthOptions`). An internal-plaintext-metadata topology stays out of scope — relax the platform
guard to accept it.

The operator "taking to production" checklist for both auth backchannels (inbound JWT + outbound
service token) is [ADR-0009 item 10](0009-reference-solution-target-profile.md).
