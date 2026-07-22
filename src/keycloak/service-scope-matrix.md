# Service-to-Service Auth — Client ↔ Scope Matrix

Companion to [ADR-0010: Service-to-Service Authentication via OAuth2 Client Credentials](../../docs/adr/0010-service-to-service-auth.md) and the `clientScopes` + 7 per-BC clients (2 service-account, 5 inbound-only) defined in [realm-export.json](realm-export.json).

## Conventions

- **Realm:** `dotnetatlas` (NOT `eshop` — see drift note below).
- **Issuer / Authority:** `http://localhost:9011/realms/dotnetatlas` (local dev).
- **Scope naming:** dot-separated, `<bc>.<verb>` (e.g. `catalog.read`, `inventory.write`). Scopes gate inbound HTTP endpoints (e.g. `inventory.write` gates the Inventory `Receive`/`Adjust` admin endpoints; `catalog.write` gates Catalog mutations). Kafka command topics have no application-layer scope check — the trust boundary is the docker network per ADR-0009.
- **Audience (RFC 9068/8707 — audience = the resource being called):** each resource **client scope** carries an `oidc-audience-mapper` stamping the owning service, so a token requesting `catalog.read` gets `"aud": "catalog-service"` no matter which client requested it; multiple scopes yield a multi-valued `aud` array. Each service validates `Audience = <this-service>` inbound. Service clients have **no** per-client `audience-self` mapper — a caller's token must be audienced for the callee, not itself (see ADR-0010 §"Audience names the callee, not the caller"). No client self-audiences — not even the user-facing app client (the SPA the user signs into, distinct from the `bff` service-account client): its token is audienced for `bff` (its inbound BFF edge) and `notifications-service` (the bell), never for itself. That client is not in the realm today; it returns with the SPA/BFF build.
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

All 7 per-BC clients are `publicClient: false`. Two — `basket`, `bff` — set `serviceAccountsEnabled: true`, use only the client-credentials grant (standard/direct-access/implicit flows disabled), and carry a committed dev secret, because their BCs make outbound cross-BC HTTP calls (`AddServiceAuth`). The other five — `catalog`, `ordering`, `inventory`, `payments`, `invoicing` — are `serviceAccountsEnabled: false` with no secret: they expose only inbound endpoints, so their `aud: <bc>-service` identity comes from the resource client-scope's `oidc-audience-mapper` (see Audience above), not a service account. A client flips to a service account the day its BC's code calls `AddServiceAuth`, not before — a service account + secret with no consumer is dead config (ADR-0010 §amendment 2026-05-27).

### `catalog-service`

- **Audience:** `catalog-service`
- **Outbound:** none — Catalog v1 makes no cross-BC HTTP calls, so its client is `serviceAccountsEnabled: false` (inbound-only) with no secret and no `optionalClientScopes`. `catalog.write` is a **human-admin** scope obtained through the `dotnetatlas-swagger` client (§`dotnetatlas-swagger`), not an outbound scope on this client.
- **Inbound (must validate on `AddJwtBearer`):** `catalog.read` (reads); **`admin` role + `catalog.write` scope** (writes)
  - Any service calling `GET /api/v1/catalog/...` with a service-account token must present `catalog.read` (a token bearing `catalog.write` also satisfies the read policy). The admin write/mutation endpoints (CreateProduct, UpdateProductPrice, Discontinue, Reactivate, CreateCategory, ReparentCategory, admin product search, DescribeProduct) require the **`admin` realm role AND the `catalog.write` scope** (defense-in-depth, mirroring `inventory-service`; see [`AuthPolicies`](../../services/Catalog/Catalog.Api/Common/Authorization/AuthPolicies.cs)). An admin obtains the scope by requesting `catalog.write` through the `dotnetatlas-swagger` client; the role gate blocks non-admins.
- **Cross-refs:** `bff.md §3.1` (BFF → Catalog reads), `basket.md` (Basket ACL → Catalog).

### `basket-service`

- **Audience:** `basket-service`
- **Outbound:** `catalog.read`
  - `catalog.read` — Basket's `IProductCatalogQueryPort` ACL adapter reads product snapshots from Catalog.
- **Inbound:** `basket.read`, `basket.write`
  - **All** basket access is via the BFF (RFC 8693 token exchange): reads via `basket.read`, mutations + checkout via `basket.write`. The user-facing app client carries **no** `basket.*` scope — consumer basket access is BFF-mediated, there is no direct SPA→Basket path ([bff.md §2.5/§3.6/§4.2](../../docs/bc-design/bff.md)). So a user JWT never carries `aud: basket-service`; the only token Basket accepts is the BFF's exchanged one. **Invariant — do not add `basket.*` to the user-facing app client** to silence a direct-call 401: it would re-mint user tokens audienced for Basket and reopen the direct-path bypass the BFF mediation closes ([ADR-0010 §amendment](../../docs/adr/0010-service-to-service-auth.md#amendment-2026-06-06--bff-token-exchange-for-buyer-scoped-callees)).
- **Cross-refs:** `bff.md §3.2/§3.6`, `basket.md`.

### `ordering-service`

- **Audience:** `ordering-service`
- **Outbound:** none — order-state-change notifications are published via the Kafka outbox (no service token); the client is `serviceAccountsEnabled: false` (inbound-only) with no secret.
- **Inbound:** `ordering.read` (reads — audience only, not a scope policy); **`admin` role only** (admin writes)
  - The BFF reads orders with an `ordering.read`-scoped token, but Ordering enforces **no read-scope policy** — `ordering.read`'s only job here is to stamp `aud: ordering-service`. The order-read endpoints (`GetOrderById`, `GetOrdersByBuyer`) set no `Policies(...)`; ownership is enforced **in the handler** (buyer-self from the JWT `sub`, cross-buyer → 404). That `sub`-dependence is why the BFF's order reads use **RFC 8693 token exchange** to preserve the buyer `sub` ([ADR-0010 § BFF token exchange](../../docs/adr/0010-service-to-service-auth.md#amendment-2026-06-06--bff-token-exchange-for-buyer-scoped-callees)), not a plain service token. The admin endpoints (MarkOrderShipped, MarkOrderDelivered) are **role-only** — they are pure human-admin actions with no service-delegation dimension, so no `ordering.write` scope is defined (ADR-0010 §"Role vs scope canonical model"). An admin reaches them with the `admin` role obtained through the `dotnetatlas-swagger` client. Saga commands enter via Kafka on `ordering.order-commands`; no application-layer scope check on that path (ADR-0009 single-trust-zone).
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
- **Inbound:** `invoicing.read` (reads — audience only, not a scope policy); **`admin` role only** (admin resend)
  - Invoice reads carry an `invoicing.read`-scoped token, but Invoicing enforces **no read-scope policy** — `invoicing.read`'s only job is to stamp `aud: invoicing-service`. The read endpoints (`GetInvoiceById`, `GetInvoiceByOrderId`) set no `Policies(...)`; ownership is enforced **in the handler** (buyer-self from the JWT `sub` via `GetBuyerIdOrNull`, cross-buyer → 404) — so the BFF's invoice reads (planned order-summary enrichment) use **RFC 8693 token exchange** to preserve the buyer `sub` ([ADR-0010 § BFF token exchange](../../docs/adr/0010-service-to-service-auth.md#amendment-2026-06-06--bff-token-exchange-for-buyer-scoped-callees)), not a plain service token. ResendInvoice is **role-only** — a pure human-admin action with no service-delegation dimension, so no `invoicing.write` scope is defined (ADR-0010 §"Role vs scope canonical model"); an admin reaches it with the `admin` role through the `dotnetatlas-swagger` client. Invoicing is otherwise projection-driven — it consumes `OrderConfirmedEvent` + `PaymentCapturedEvent` from Kafka event topics.
- **Cross-refs:** `invoicing.md §8`, ADR-0017/0018/0019.

### `bff`

- **Audience (inbound):** `bff` — the BFF validates inbound **user** JWTs against `ValidAudience = bff` (pinned in `EShop.BFF.Api/appsettings.json`); the user-facing client stamps `aud: bff` via its `audience-bff` mapper. The `bff` **client** still carries **no self-audience mapper on its outbound tokens** — those are audienced for the **callee** BC via the requested scope (e.g. `catalog.read` → `aud: catalog-service`).
- **Token exchange (Standard / RFC 8693):** the `bff` client sets `standard.token.exchange.enabled = "true"` — Keycloak 26.3.2 **Standard Token Exchange v2** (GA / default-on; **not** the legacy `token-exchange` feature, **no** FGAP). v2 only exchanges a `subject_token` carrying `bff` in its `aud` (hence the `audience-bff` mapper above), and re-audiences the exchanged token to the callee via the requested scope while preserving the user `sub`. See [ADR-0010 § Implementation — Standard Token Exchange v2](../../docs/adr/0010-service-to-service-auth.md#implementation--keycloak-standard-token-exchange-v2-landed-329).
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
- **Audience (4 unconditional client-level mappers):** `bff`, `ordering-service`, `invoicing-service`,
  `notifications-service`. The swagger client stamps a BC `aud` on **every** token it issues only where a
  human admin reaches the BC with **no scope** to carry that audience:
  - `ordering-service` / `invoicing-service` — the **role-only** admin endpoints (ship / deliver / resend).
    ADR-0010 §"Role vs scope canonical model" defines no `ordering.write` / `invoicing.write` scope, so the
    client-level mapper is the **only** audience source; drop it and those admin endpoints become
    unreachable through a real human login (JwtBearer `ValidateAudience` rejects before the role gate runs).
  - `notifications-service` — the `[Authorize]`-only in-app bell hub (reached directly, no scope; see Notifications below).
  - `bff` — the Standard Token Exchange v2 holder constraint (the user `subject_token` must carry the requester
    `bff` in its `aud`; see the `bff` block above).

  The **role + scope** BCs — `catalog-service`, `inventory-service`, `payments-service` — carry **no**
  client-level audience mapper: an admin can only reach them by requesting the matching optional scope
  (`catalog.write` / `inventory.write` / `payments.read`), whose own `oidc-audience-mapper` already stamps the
  callee `aud`, so an unconditional client-level one would be redundant. `basket-service` carries none either —
  basket is **100 % BFF-mediated**, no direct admin path. The role gate, not the audience, is what blocks
  non-admins; the future SPA app client stamps **no resource-BC audience** at all — its only client-level
  audiences are `bff` (BFF edge) and `notifications-service` (bell) (Basket invariant above +
  [ADR-0010 §2026-06-06](../../docs/adr/0010-service-to-service-auth.md#amendment-2026-06-06--bff-token-exchange-for-buyer-scoped-callees)).
- **Subject (`sub`):** the client carries an explicit `oidc-sub-mapper` (`subject`) so the **access
  token** carries `sub`. This realm is scope-light — it has no Keycloak built-in `basic` client scope
  (which would otherwise supply the access-token `sub`), so without this mapper the access token has
  **no `sub`** (only the id token would). The role-gated admin endpoints never noticed (they gate on
  `roles`), but every `sub`-keyed surface broke when driven from Swagger: the bell hub drops a
  `sub`-less connection ([SubClaimUserIdProvider](../../services/Notifications/Notifications.Api/SignalRHubs/SubClaimUserIdProvider.cs)),
  and the buyer-self reads in Ordering / Invoicing (`GetBuyerIdOrNull` → `sub`) `401`'d on identity
  resolution. The `subject` mapper fixes all of them.

### `notifications` (in-app bell — user-facing, NOT a service client)

Notifications' only inbound surface is the in-app **bell** SignalR hub at `/hubs/v1/notifications`
([notifications.md §6](../../docs/bc-design/notifications.md), [ADR-0032](../../docs/adr/0032-notifications-dispatch-and-channels.md)).
It has **no Keycloak client and no scope**: nothing calls it service-to-service, and the hub is
`[Authorize]`-only (any authenticated user, recipient keyed on the token's own `sub`), so a scope
would gate nothing while stamping an audience with no policy behind it — the dead config ADR-0010
§"Role vs scope canonical model" rejects.

- **Audience:** `notifications-service`, pinned fail-closed in `Notifications.Api/appsettings.json`
  (`Authentication:JwtBearer:…:ValidAudience`).
- **Access shape — a third auth category.** Unlike the BFF-mediated buyer-scoped BCs (Basket / Ordering /
  Invoicing, reached via RFC 8693 token exchange), the bell is reached **browser → YARP edge → hub** with
  YARP a *transparent WebSocket proxy*: the **user's own token rides through verbatim** and must itself
  carry `aud: notifications-service` ([ADR-0035 §Rationale](../../docs/adr/0035-edge-owned-cors-yarp.md) —
  "the token rides the query string *through* YARP and each BC still validates its own JWT"). No token
  exchange. Safe because the recipient is the token's own `sub`, so there is no service-account
  intermediary to mis-resolve the owner — the direct-path bypass that BFF mediation closes for Basket has
  no analogue here.
- **Recipient identity — needs `sub` in the access token.** The hub keys its per-user group on `sub`
  ([SubClaimUserIdProvider](../../services/Notifications/Notifications.Api/SignalRHubs/SubClaimUserIdProvider.cs)),
  and SignalR validates the **access** token (not the id token). So a usable token needs `sub` *as well as*
  the audience — a right-`aud`/no-`sub` token authenticates, then the hub drops the connection
  (`"Connection has no user identity."`). The `dotnetatlas-swagger` `subject` mapper (above) supplies it.
- **Dev access / future SPA.** The swagger `audience-notifications` + `subject` mappers make a dev login
  usable against the bell (ADR-0010 driver #3, *laptop-testable*). The future SPA / user-facing app client
  inherits the obligation — emit `sub` and stamp `aud: notifications-service` (the bell) alongside `aud: bff`
  (its inbound BFF edge + the token-exchange holder constraint). **It must not, however, mirror the swagger
  client's human-admin BC audiences** — the swagger tool also stamps `ordering-service` / `invoicing-service`
  for its role-only admin endpoints, and carrying those on a real SPA client would re-mint user tokens audienced
  for Ordering/Invoicing and reopen the direct consumer→BC path the BFF token exchange deliberately closes.
  Cross-origin browser reach additionally waits on the YARP edge's CORS policy
  ([ADR-0035](../../docs/adr/0035-edge-owned-cors-yarp.md)).
- **Counts:** `notifications-service` is an audience *value* only — it adds **no** client and **no** scope,
  so the 7 per-BC clients / 8 declared clients / 9 scopes above are unchanged.

---

## 3. Production handoff

### Dev-only secrets

`realm-export.json` commits two literal client secrets of the form `dev-<service>-secret-rotate-in-prod` — one per service-account client (`basket`, `bff`). **These are acceptable ONLY for local Docker dev** — every non-local environment MUST regenerate each secret.

**Why committed literal (and not templated):** Keycloak's `--import-realm` does not perform `${ENV_VAR}` substitution on realm-export.json. Committing placeholders would require adding a pre-mount substitution layer (custom entrypoint or `envsubst` preprocessing); that complexity is out of Wave 0 scope. The pattern matches every committed service-client secret in `realm-export.json` (e.g. `basket-service` → `dev-basket-service-secret-rotate-in-prod`).

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
  update "clients/$(docker exec keycloak9011 /opt/keycloak/bin/kcadm.sh get clients -r dotnetatlas -q clientId=basket-service --fields id --format csv --noquotes | tail -1)/client-secret" \
  -r dotnetatlas -s value=<new-secret>
```

### Re-importing the realm after editing `realm-export.json`

Keycloak's `--import-realm` flag runs only on first container start when the `keycloak` Postgres database has no `dotnetatlas` realm. To re-apply edits:

```bash
docker compose --profile full stop keycloak
docker exec postgres5433 psql -U postgres -c "DROP DATABASE IF EXISTS keycloak WITH (FORCE);"
docker exec postgres5433 psql -U postgres -c "CREATE DATABASE keycloak;"
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

# List all clients — expect 8 realm-declared (plus Keycloak builtins: account, account-console, admin-cli, broker, realm-management, security-admin-console)
curl -s "http://localhost:9011/admin/realms/dotnetatlas/clients" \
  -H "Authorization: Bearer $TOKEN" \
  | python -c "import sys,json;cs=json.load(sys.stdin);ours={'dotnetatlas-swagger','catalog-service','basket-service','ordering-service','inventory-service','payments-service','invoicing-service','bff'};print([c['clientId'] for c in cs if c['clientId'] in ours])"

# List all client scopes — expect the 9 declared scopes plus Keycloak defaults
curl -s "http://localhost:9011/admin/realms/dotnetatlas/client-scopes" \
  -H "Authorization: Bearer $TOKEN" \
  | python -c "import sys,json;ss={s['name'] for s in json.load(sys.stdin)};ours={'catalog.read','catalog.write','basket.read','basket.write','ordering.read','inventory.read','inventory.write','payments.read','invoicing.read'};print('found',len(ours&ss),'of',len(ours));print('missing:',ours-ss)"

# dotnetatlas-swagger must stamp exactly 4 unconditional audiences: bff (token-exchange subject) +
# ordering/invoicing-service (role-only admin endpoints) + notifications-service (bell hub) — and NOT
# basket/catalog/inventory/payments-service (basket is BFF-mediated; the other three ride their optional scope)
SW=$(curl -s "http://localhost:9011/admin/realms/dotnetatlas/clients?clientId=dotnetatlas-swagger" \
  -H "Authorization: Bearer $TOKEN" | python -c "import sys,json;print(json.load(sys.stdin)[0]['id'])")
curl -s "http://localhost:9011/admin/realms/dotnetatlas/clients/$SW/protocol-mappers/models" \
  -H "Authorization: Bearer $TOKEN" \
  | python -c "import sys,json;auds=sorted(m['config']['included.client.audience'] for m in json.load(sys.stdin) if m['protocolMapper']=='oidc-audience-mapper');print('swagger audiences:',auds);print('exactly the 4 expected:',auds==['bff','invoicing-service','notifications-service','ordering-service']);print('no bypass audiences:',not ({'basket-service','catalog-service','inventory-service','payments-service'} & set(auds)))"

# Service-account token minted as basket-service asking for the catalog.read scope.
# Caller (basket-service) != callee (catalog-service) on purpose: it proves the aud
# rides the requested scope's mapper, not the caller identity — a check catalog-service
# asking for catalog.write could not make (there caller and callee coincide).
curl -s -X POST http://localhost:9011/realms/dotnetatlas/protocol/openid-connect/token \
  -d 'grant_type=client_credentials' \
  -d 'client_id=basket-service' \
  -d 'client_secret=dev-basket-service-secret-rotate-in-prod' \
  -d 'scope=catalog.read' \
  | python -c "import sys,json,base64;t=json.load(sys.stdin)['access_token'];p=t.split('.')[1];p+='='*(4-len(p)%4);d=json.loads(base64.urlsafe_b64decode(p));print('azp',d.get('azp'),'aud',d.get('aud'),'scope',d.get('scope'))"
```

Expected result on the basket-service check: `azp basket-service aud catalog-service scope <...> catalog.read` — caller ≠ callee, so the `aud` claim proves the `catalog.read` scope's `audience-catalog-service` mapper stamps the **callee**, not the caller. The swagger-mapper check prints exactly the four unconditional audiences `['bff', 'invoicing-service', 'notifications-service', 'ordering-service']` with `exactly the 4 expected: True` and `no bypass audiences: True` — the bell hub (`notifications-service`) and the role-only Ordering/Invoicing admin endpoints stay reachable through a real human login, while basket/catalog/inventory/payments are NOT stamped unconditionally ([§ Notifications](#notifications-in-app-bell--user-facing-not-a-service-client)).

---

## 5. Open follow-ups (Wave 0 DoD or later)

1. **Secret rotation playbook** — formalize the kcadm.sh rotation recipe above into a runbook when production infra is in scope.
