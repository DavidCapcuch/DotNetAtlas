# Service-to-Service Auth — Client ↔ Scope Matrix

Companion to [ADR-0010: Service-to-Service Authentication via OAuth2 Client Credentials](../../docs/adr/0010-service-to-service-auth.md) and the `clientScopes` + 7 service clients defined in [realm-export.json](realm-export.json).

## Conventions

- **Realm:** `dotnetatlas` (NOT `eshop` — see drift note below).
- **Issuer / Authority:** `http://localhost:9011/realms/dotnetatlas` (local dev).
- **Scope naming:** dot-separated, `<bc>.<verb>` (e.g. `catalog.read`, `inventory.write`). Scopes gate inbound HTTP endpoints (e.g. `inventory.write` gates the Inventory `Receive`/`Adjust` admin endpoints; `catalog.write` gates Catalog mutations). Kafka command topics have no application-layer scope check — the trust boundary is the docker network per ADR-0009.
- **Audience (RFC 9068/8707 — audience = the resource being called):** each resource **client scope** carries an `oidc-audience-mapper` stamping the owning service, so a token requesting `catalog.read` gets `"aud": "catalog-service"` no matter which client requested it; multiple scopes yield a multi-valued `aud` array. Each service validates `Audience = <this-service>` inbound. Service clients have **no** per-client `audience-self` mapper — a caller's token must be audienced for the callee, not itself (corrected 2026-05-27; see ADR-0010 §"Keycloak audience lives on the client SCOPE"). Only the user-facing app client + Swagger self-audience (`e9fdb985…`), since there the app is the resource.
- **Token endpoint:** `POST http://localhost:9011/realms/dotnetatlas/protocol/openid-connect/token` with `grant_type=client_credentials`, `client_id`, `client_secret`, `scope`.
- **Production rotation:** dev-only secrets are committed literally in `realm-export.json` — **every service client secret must be rotated for any non-local environment.** See §3.

### Drift note (historical implementation prompt)

`docs/implementation-prompts/wave-0-platform-prep.md:280` references `realms/eshop` (port `8081`). These strings predate the realm-naming and port-allocation decisions; the authoritative values are `realms/dotnetatlas` and port `9011`. The implementation-prompts directory is frozen historical per the repo convention, so the stale strings stay there — anywhere the historical doc is cited at face value, substitute the live values.

---

## 1. Scope catalog

9 scopes are defined in the top-level `clientScopes` block of `realm-export.json`. All use `protocol: openid-connect`, `display.on.consent.screen: false`, `include.in.token.scope: true`.

### Catalog (2)

| Scope | Description |
|---|---|
| `catalog.read` | Read product and category metadata. |
| `catalog.write` | Create or update products and categories. |

### Basket (2)

| Scope | Description |
|---|---|
| `basket.read` | Read basket contents. |
| `basket.write` | Mutate basket (add/remove items, change quantity, checkout). |

### Ordering (1)

| Scope | Description |
|---|---|
| `ordering.read` | Read order details and status. |

### Inventory (2)

| Scope | Description |
|---|---|
| `inventory.read` | Read stock levels and reservation status. |
| `inventory.write` | Scope half of the Inventory `Receive` / `Adjust` admin write gate — required **alongside** the `admin` realm role (defense-in-depth). |

### Payments (1)

| Scope | Description |
|---|---|
| `payments.read` | Read payment transaction status. |

### Invoicing (1)

| Scope | Description |
|---|---|
| `invoicing.read` | Read invoice and credit-note details; download invoice PDF. |

**Total: 9 scopes.**

---

## 2. Per-service blocks

Each of the 7 service clients uses `serviceAccountsEnabled: true`, `publicClient: false`, and only the client-credentials grant (standard/direct-access/implicit flows disabled).

### `catalog-service`

- **Audience:** `catalog-service`
- **Outbound (acquirable via `optionalClientScopes`):** `catalog.write`
  - `catalog.write` — reserved for administrative write paths that treat Catalog's own API as a service callee.
- **Inbound (must validate on `AddJwtBearer`):** `catalog.read` (reads); **`admin` role + `catalog.write` scope** (writes)
  - Any service calling `GET /api/v1/catalog/...` with a service-account token must present `catalog.read` (a token bearing `catalog.write` also satisfies the read policy). The admin write/mutation endpoints (CreateProduct, UpdateProductPrice, Discontinue, Reactivate, CreateCategory, ReparentCategory, admin product search, DescribeProduct) require the **`admin` realm role AND the `catalog.write` scope** (defense-in-depth, mirroring `inventory-service`; see [`AuthPolicies`](../../services/Catalog/Catalog.Api/Common/Authorization/AuthPolicies.cs)). An admin obtains the scope by requesting `catalog.write` through the `dotnetatlas-swagger` client; the role gate blocks non-admins.
- **Cross-refs:** `bff.md §3.1` (BFF → Catalog reads), `basket.md` (Basket ACL → Catalog).

### `basket-service`

- **Audience:** `basket-service`
- **Outbound:** `catalog.read`
  - `catalog.read` — Basket's `IProductCatalogQueryPort` ACL adapter reads product snapshots from Catalog.
- **Inbound:** `basket.read`, `basket.write`
  - **All** basket access is via the BFF (RFC 8693 token exchange): reads via `basket.read`, mutations + checkout via `basket.write`. The user-facing app client (`e9fdb985`) carries **no** `basket.*` scope — consumer basket access is BFF-mediated, there is no direct SPA→Basket path ([bff.md §2.5/§3.6/§4.2](../../docs/bc-design/bff.md)). So a user JWT never carries `aud: basket-service`; the only token Basket accepts is the BFF's exchanged one.
- **Cross-refs:** `bff.md §3.2/§3.6`, `basket.md`.

### `ordering-service`

- **Audience:** `ordering-service`
- **Outbound:** none — order-state-change notifications are published via the Kafka outbox (no service token).
- **Inbound:** `ordering.read` (reads); **`admin` role only** (admin writes)
  - BFF reads orders via `ordering.read`. The admin endpoints (MarkOrderShipped, MarkOrderDelivered) are **role-only** — they are pure human-admin actions with no service-delegation dimension, so no `ordering.write` scope is defined (ADR-0010 §"Role vs scope canonical model"). An admin reaches them with the `admin` role obtained through the `dotnetatlas-swagger` client. Saga commands enter via Kafka on `ordering.order-commands`; no application-layer scope check on that path (ADR-0009 single-trust-zone).
- **Cross-refs:** `bff.md §3.3`, `events-catalog.md §2` (Ordering Commands).

### `inventory-service`

- **Audience:** `inventory-service`
- **Outbound:** none — low-stock notifications are published via the Kafka outbox (no service token).
- **Inbound:** `inventory.read` (reads); **`admin` role + `inventory.write` scope** (writes)
  - BFF reads stock via `inventory.read`. The admin `Receive` / `Adjust` endpoints require the **`admin` realm role AND the `inventory.write` scope** (defense-in-depth, mirroring `PaymentsAdmin`; see [`AuthPolicies`](../../services/Inventory/Inventory.Api/Common/Authorization/AuthPolicies.cs)). An admin obtains the scope by requesting `inventory.write` through the `dotnetatlas-swagger` client; the role gate blocks non-admins. Saga reservation commands enter via Kafka on `inventory.reservation-commands`; no application-layer scope check on that path.
- **Cross-refs:** `bff.md §3.1/3.3`, `events-catalog.md §2` (Inventory Reservation Commands).

### `payments-service`

- **Audience:** `payments-service`
- **Outbound:** none — payment-failure / refund-issued notifications are published via the Kafka outbox (no service token).
- **Inbound:** **`admin` role + `payments.read` scope** (human-admin reads)
  - Payments exposes human-admin HTTP **read** endpoints — `GET /api/v1/payments/{id}` and `GET /api/v1/payments?orderId=...` — gated on the **`admin` realm role AND the `payments.read` scope** (defense-in-depth; see [`AuthPolicies`](../../services/Payments/Payments.Api/Common/Authorization/AuthPolicies.cs)). No service calls Payments over HTTP — the BFF does not depend on Payments and payment commands arrive via Kafka on `payments.commands` (no application-layer scope check on that path). The human admin obtains `payments.read` through the `dotnetatlas-swagger` client; the role gate blocks non-admins. Payments has no HTTP **write** surface in v1.
- **Cross-refs:** `events-catalog.md §2` (Payments Commands), ADR-0005 (payments webhook if present).

### `invoicing-service`

- **Audience:** `invoicing-service`
- **Outbound:** none — invoice-issued / credit-note-issued notifications are published via the Kafka outbox (no service token).
- **Inbound:** `invoicing.read` (reads); **`admin` role only** (admin resend)
  - HTTP reads (invoice detail, PDF download) plus one admin action, ResendInvoice, which is **role-only** — a pure human-admin action with no service-delegation dimension, so no `invoicing.write` scope is defined (ADR-0010 §"Role vs scope canonical model"). An admin reaches it with the `admin` role obtained through the `dotnetatlas-swagger` client. Invoicing is projection-driven — it consumes `OrderConfirmedEvent` + `PaymentCapturedEvent` from Kafka event topics and does not require per-BC read scopes.
- **Cross-refs:** `invoicing.md §8`, ADR-0017/0018/0019.

### `bff`

- **Audience:** none — BFF validates user JWTs, not service tokens, so it is never a resource server (no self-audience mapper since 2026-05-27). Its outbound tokens are audienced for the **callee** BC via the requested scope (e.g. `catalog.read` → `aud: catalog-service`).
- **Outbound:** 6 scopes — every cross-BC read + `basket.write`:
  - `catalog.read`, `basket.read`, `basket.write`, `ordering.read`, `inventory.read`, `invoicing.read`
  - The BFF is the primary HTTP caller of the five BCs it fronts (Catalog, Basket, Ordering, Inventory, Invoicing); it does **not** call Payments over HTTP (payment commands/results are async via Kafka). Catalog has one other service-to-service caller — `basket-service`'s ACL adapter reads product snapshots via `catalog.read`.
  - **Buyer-scoped callees** (`basket.read`, `basket.write`, `ordering.read`, `invoicing.read` — Basket / Ordering / Invoicing derive the resource owner from the token `sub`) are reached via **RFC 8693 token exchange** so the buyer `sub` is preserved; a plain `client_credentials` token would carry the BFF service account's `sub` and resolve the wrong buyer. The non-buyer-scoped reads (`catalog.read`, `inventory.read`) take a plain service token. See [ADR-0010 § BFF token exchange](../../docs/adr/0010-service-to-service-auth.md#amendment-2026-06-06--bff-token-exchange-for-buyer-scoped-callees) + [bff.md § 2.3](../../docs/bc-design/bff.md), decided [#323](https://github.com/DavidCapcuch/DotNetAtlas/issues/323).
- **Inbound:** none — user-facing only; inbound requests carry user JWTs (validated against `dotnetatlas` realm user-auth, not service-auth).
- **Cross-refs:** `bff.md §3.1–3.4`.

### `dotnetatlas-swagger` (human admin — NOT a service client)

The Swagger UI client (`publicClient: true`, authorization-code + PKCE) is the canonical way a
**human admin** acquires admin scopes — it is not a `client_credentials` service client. Its
`optionalClientScopes` carries exactly the admin scopes a human operator needs to exercise the
role + scope admin endpoints:

- **`optionalClientScopes`:** `offline_access`, `inventory.write`, `catalog.write`, `payments.read`
  - `inventory.write` → Inventory `Receive` / `Adjust` (with the `admin` role).
  - `catalog.write` → Catalog admin write/mutation endpoints (with the `admin` role).
  - `payments.read` → Payments admin read endpoints (with the `admin` role).
- **Why no read scopes** (`catalog.read`, `inventory.read`, `ordering.read`, `invoicing.read`,
  `basket.*`): those are service-delegation scopes carried by the BFF, not the human admin. An
  admin holding the write scope already satisfies the corresponding read policy (write implies
  read), and role-only admin endpoints (Ordering, Invoicing) need no scope at all. Adding read
  scopes here would be provisioned-for-convenience dead config (ADR-0010 §"Role vs scope
  canonical model").
- **Audience:** the swagger client has per-client `oidc-audience-mapper`s for every BC (a dev-only
  convenience so one login works across services); the role gate, not the audience, is what blocks
  non-admins from admin endpoints.

---

## 3. Production handoff

### Dev-only secrets

`realm-export.json` commits nine literal client secrets of the form `dev-<service>-secret-rotate-in-prod`. **These are acceptable ONLY for local Docker dev** — every non-local environment MUST regenerate each secret.

**Why committed literal (and not templated):** Keycloak's `--import-realm` does not perform `${ENV_VAR}` substitution on realm-export.json. Committing placeholders would require adding a pre-mount substitution layer (custom entrypoint or `envsubst` preprocessing); that complexity is out of Wave 0 scope. The pattern matches the existing backend-client secret `realm-export.json:100` (`super-secret-secret-that-should-be-regenerated-for-production`).

### Rotating a service secret

Via admin console:
1. Log in at `http://localhost:9011` as `admin` / `admin`.
2. Realm `dotnetatlas` → Clients → select `<service>` → Credentials → Regenerate.
3. Copy the new secret to the target environment's secret store.

Via `kcadm.sh` (scripted):
```bash
docker exec -it keycloak9011 /opt/keycloak/bin/kcadm.sh \
  config credentials --server http://localhost:8080 --realm master --user admin --password admin
docker exec -it keycloak9011 /opt/keycloak/bin/kcadm.sh \
  update "clients/$(docker exec keycloak9011 /opt/keycloak/bin/kcadm.sh get clients -r dotnetatlas -q clientId=catalog-service --fields id --format csv --noquotes | tail -1)/client-secret" \
  -r dotnetatlas -s value=<new-secret>
```

### Env-var convention (consumed by `ClientCredentialsTokenHandler`, Wave 0 M3)

Services read their own secret from the env-var pattern:

| Service | Env var | Value source |
|---|---|---|
| `catalog-service` | `KEYCLOAK__SERVICE_CLIENT_SECRET__CATALOG` | compose `.env` (dev) or vault (prod) |
| `basket-service` | `KEYCLOAK__SERVICE_CLIENT_SECRET__BASKET` | ″ |
| `ordering-service` | `KEYCLOAK__SERVICE_CLIENT_SECRET__ORDERING` | ″ |
| `inventory-service` | `KEYCLOAK__SERVICE_CLIENT_SECRET__INVENTORY` | ″ |
| `payments-service` | `KEYCLOAK__SERVICE_CLIENT_SECRET__PAYMENTS` | ″ |
| `invoicing-service` | `KEYCLOAK__SERVICE_CLIENT_SECRET__INVOICING` | ″ |
| `bff` | `KEYCLOAK__SERVICE_CLIENT_SECRET__BFF` | ″ |

Wave 0 **M7** wires these env-vars into per-service `appsettings.*.json` + compose `environment` blocks.

### Re-importing the realm after editing `realm-export.json`

Keycloak's `--import-realm` flag runs only on first container start when the `keycloak` Postgres database has no `dotnetatlas` realm. To re-apply edits:

```bash
docker compose --profile full stop keycloak
docker exec postgresdb psql -U postgres -c "DROP DATABASE IF EXISTS keycloak WITH (FORCE);"
docker exec postgresdb psql -U postgres -c "CREATE DATABASE keycloak;"
docker compose --profile full up -d keycloak
# wait for healthy:
docker compose logs -f keycloak | grep -m1 "Imported realm"
```

Alternatively, diff changes via the admin console or `kcadm.sh` and merge them into the running realm without dropping the DB.

---

## 4. Verification

Quick realm-state sanity check (after `docker compose --profile full up -d keycloak`):

```bash
# OIDC discovery
curl -s http://localhost:9011/realms/dotnetatlas/.well-known/openid-configuration \
  | python -c "import sys,json;print(json.load(sys.stdin)['issuer'])"

# Acquire admin token
TOKEN=$(curl -s -X POST http://localhost:9011/realms/master/protocol/openid-connect/token \
  -d 'client_id=admin-cli' -d 'username=admin' -d 'password=admin' -d 'grant_type=password' \
  | python -c "import sys,json;print(json.load(sys.stdin)['access_token'])")

# List all clients — expect 9 realm-declared (plus Keycloak builtins: account, admin-cli, broker, realm-management, security-admin-console)
curl -s "http://localhost:9011/admin/realms/dotnetatlas/clients" \
  -H "Authorization: Bearer $TOKEN" \
  | python -c "import sys,json;cs=json.load(sys.stdin);ours={'e9fdb985-9173-4e01-9d73-ac2d60d1dc8e','dotnetatlas-swagger','catalog-service','basket-service','ordering-service','inventory-service','payments-service','invoicing-service','bff'};print([c['clientId'] for c in cs if c['clientId'] in ours])"

# List all client scopes — expect the 9 declared scopes plus Keycloak defaults
curl -s "http://localhost:9011/admin/realms/dotnetatlas/client-scopes" \
  -H "Authorization: Bearer $TOKEN" \
  | python -c "import sys,json;ss={s['name'] for s in json.load(sys.stdin)};ours={'catalog.read','catalog.write','basket.read','basket.write','ordering.read','inventory.read','inventory.write','payments.read','invoicing.read'};print('found',len(ours&ss),'of',len(ours));print('missing:',ours-ss)"

# Service-account token for catalog-service with scope catalog.write
curl -s -X POST http://localhost:9011/realms/dotnetatlas/protocol/openid-connect/token \
  -d 'grant_type=client_credentials' \
  -d 'client_id=catalog-service' \
  -d 'client_secret=dev-catalog-service-secret-rotate-in-prod' \
  -d 'scope=catalog.write' \
  | python -c "import sys,json,base64;t=json.load(sys.stdin)['access_token'];p=t.split('.')[1];p+='='*(4-len(p)%4);d=json.loads(base64.urlsafe_b64decode(p));print('azp',d.get('azp'),'aud',d.get('aud'),'scope',d.get('scope'))"
```

Expected result on the last check: `azp catalog-service aud catalog-service scope <...> catalog.write` — the `aud` claim confirms the `catalog.write` scope's `audience-catalog-service` mapper fired. (Cross-service check: mint as `basket-service` with `scope=catalog.read` → `aud catalog-service`, proving the audience follows the scope/callee, not the caller.)

---

## 5. Open follow-ups (Wave 0 DoD or later)

1. **Wave 0 M7** — wire the nine `KEYCLOAK__SERVICE_CLIENT_SECRET__*` env vars into compose `environment` blocks and per-service `appsettings.*.json` so `ClientCredentialsTokenHandler` (from M3) can acquire tokens at runtime.
2. **Secret rotation playbook** — formalize the kcadm.sh rotation recipe above into a runbook when production infra is in scope.
