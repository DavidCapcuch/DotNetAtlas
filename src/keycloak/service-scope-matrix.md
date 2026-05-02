# Service-to-Service Auth — Client ↔ Scope Matrix

Companion to [ADR-0010: Service-to-Service Authentication via OAuth2 Client Credentials](../../docs/adr/0010-service-to-service-auth.md) and the `clientScopes` + 9 service clients defined in [realm-export.json](realm-export.json).

## Conventions

- **Realm:** `dotnetatlas` (NOT `eshop` — see drift note below).
- **Issuer / Authority:** `http://localhost:9011/realms/dotnetatlas` (local dev).
- **Scope naming:** dot-separated, `<bc>.<verb>` or `<bc>.commands.<verb>`. Read/write scopes gate HTTP endpoints; `commands.*` scopes gate Kafka command-topic producers.
- **Audience:** every service client has an `audience-self` OIDC mapper so service-account tokens carry `"aud": "<own-clientId>"`. Inbound validation in each service enforces `Audience = <this-service>` (ADR-0010 L99).
- **Token endpoint:** `POST http://localhost:9011/realms/dotnetatlas/protocol/openid-connect/token` with `grant_type=client_credentials`, `client_id`, `client_secret`, `scope`.
- **Production rotation:** dev-only secrets are committed literally in `realm-export.json` — **every service client secret must be rotated for any non-local environment.** See §4.

### Drift note (ADR-0010 + wave-0-platform-prep)

ADR-0010 L94 + L99 and `docs/implementation-prompts/wave-0-platform-prep.md:280` reference `realms/eshop` (port `8081`). These strings predate the realm naming decision; the authoritative values are `realms/dotnetatlas` and port `9011`. Anywhere those docs are cited, substitute the live values. A sweep PR is tracked as Wave 0 DoD follow-up.

---

## 1. Scope catalog

20 scopes are defined in the top-level `clientScopes` block of `realm-export.json`. All use `protocol: openid-connect`, `display.on.consent.screen: false`, `include.in.token.scope: true`.

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

### Ordering (5)

| Scope | Description |
|---|---|
| `ordering.read` | Read order details and status. |
| `ordering.commands.create` | Publish `CreateOrderCommand` to `ordering.order-commands`. |
| `ordering.commands.confirm` | Publish `ConfirmOrderCommand` to `ordering.order-commands`. |
| `ordering.commands.cancel` | Publish `CancelOrderCommand` to `ordering.order-commands`. |
| `ordering.commands.fail` | Publish `MarkOrderFailedCommand` to `ordering.order-commands`. |

### Inventory (4)

| Scope | Description |
|---|---|
| `inventory.read` | Read stock levels and reservation status. |
| `inventory.commands.reserve` | Publish `ReserveStockCommand` to `inventory.reservation-commands`. |
| `inventory.commands.confirm` | Publish `ConfirmReservationCommand` to `inventory.reservation-commands`. |
| `inventory.commands.release` | Publish `ReleaseReservationCommand` to `inventory.reservation-commands`. |

### Payments (5)

| Scope | Description |
|---|---|
| `payments.read` | Read payment transaction status. |
| `payments.commands.authorize` | Publish `AuthorizePaymentCommand` to `payments.commands`. |
| `payments.commands.capture` | Publish `CapturePaymentCommand` to `payments.commands`. |
| `payments.commands.void` | Publish `VoidPaymentCommand` to `payments.commands`. |
| `payments.commands.refund` | Publish `RequestRefundCommand` to `payments.commands`. |

### Invoicing (1)

| Scope | Description |
|---|---|
| `invoicing.read` | Read invoice and credit-note details; download invoice PDF. |

### Notifications (1)

| Scope | Description |
|---|---|
| `notifications.commands.send` | Publish `SendEmailNotificationCommand` to `notification.commands` (via outbox publisher, not direct Kafka produce). |

**Total: 20 scopes.**

---

## 2. Per-service blocks

Each of the 9 service clients uses `serviceAccountsEnabled: true`, `publicClient: false`, and only the client-credentials grant (standard/direct-access/implicit flows disabled).

### `catalog-service`

- **Audience:** `catalog-service`
- **Outbound (acquirable via `optionalClientScopes`):** `catalog.write`, `notifications.commands.send`
  - `catalog.write` — reserved for administrative write paths that treat Catalog's own API as a service callee.
  - `notifications.commands.send` — Catalog publishes notifications (e.g., new-product announcements) via outbox.
- **Inbound (must validate on `AddJwtBearer`):** `catalog.read`, `catalog.write`
  - Any service calling `GET /api/v1/catalog/...` with a service-account token must present `catalog.read`; admin/write endpoints require `catalog.write`.
- **Cross-refs:** `bff.md §3.1` (BFF → Catalog reads), `basket.md` (Basket ACL → Catalog).

### `basket-service`

- **Audience:** `basket-service`
- **Outbound:** `catalog.read`, `notifications.commands.send`
  - `catalog.read` — Basket's `IProductCatalogQueryPort` ACL adapter reads product snapshots from Catalog.
  - `notifications.commands.send` — Basket publishes abandoned-cart notifications via outbox.
- **Inbound:** `basket.read`, `basket.write`
  - BFF reads sessions via `basket.read`; BFF mutates (add/remove/checkout) via `basket.write`.
- **Cross-refs:** `bff.md §3.2`, `basket.md`.

### `ordering-service`

- **Audience:** `ordering-service`
- **Outbound:** `notifications.commands.send`
  - Ordering publishes order-state-change notifications via outbox.
- **Inbound:** `ordering.read`, `ordering.commands.create`, `ordering.commands.confirm`, `ordering.commands.cancel`, `ordering.commands.fail`
  - BFF reads orders via `ordering.read`; the Checkout Saga is the sole producer of every `ordering.commands.*` scope (see `ADR-0004`).
- **Cross-refs:** `bff.md §3.3`, `events-catalog.md §2` (Ordering Commands).

### `inventory-service`

- **Audience:** `inventory-service`
- **Outbound:** `notifications.commands.send`
  - Inventory publishes low-stock notifications via outbox.
- **Inbound:** `inventory.read`, `inventory.commands.reserve`, `inventory.commands.confirm`, `inventory.commands.release`
  - BFF reads stock via `inventory.read`; the Checkout Saga is the sole producer of every `inventory.commands.*` scope.
- **Cross-refs:** `bff.md §3.1/3.3`, `events-catalog.md §2` (Inventory Reservation Commands).

### `payments-service`

- **Audience:** `payments-service`
- **Outbound:** `notifications.commands.send`
  - Payments publishes payment-failure / refund-issued notifications via outbox.
- **Inbound:** `payments.read`, `payments.commands.authorize`, `payments.commands.capture`, `payments.commands.void`, `payments.commands.refund`
  - The Checkout Saga (via its `PaymentProcessingSaga` sub-orchestrator) is the sole producer of every `payments.commands.*` scope.
- **Cross-refs:** `events-catalog.md §2` (Payments Commands), ADR-0005 (payments webhook if present).

### `invoicing-service`

- **Audience:** `invoicing-service`
- **Outbound:** `notifications.commands.send`
  - Invoicing publishes invoice-issued / credit-note-issued notifications via outbox.
- **Inbound:** `invoicing.read`
  - Only HTTP reads (invoice detail, PDF download). Invoicing is projection-driven — it consumes `OrderConfirmedEvent` + `PaymentCapturedEvent` from Kafka event topics and does not require per-BC read scopes.
- **Cross-refs:** `invoicing.md §8`, ADR-0017/0018/0019.

### `checkout-saga`

- **Audience:** `checkout-saga` (vestigial; kept for symmetry)
- **Outbound:** 12 scopes — all `ordering.commands.*`, all `inventory.commands.*`, all `payments.commands.*`, plus `notifications.commands.send`:
  - `ordering.commands.create`, `ordering.commands.confirm`, `ordering.commands.cancel`, `ordering.commands.fail`
  - `inventory.commands.reserve`, `inventory.commands.confirm`, `inventory.commands.release`
  - `payments.commands.authorize`, `payments.commands.capture`, `payments.commands.void`, `payments.commands.refund`
  - `notifications.commands.send`
  - Per ADR-0004, the Checkout Saga is the ONLY orchestrator of saga commands. This is the only client that issues `*.commands.*` tokens at runtime.
- **Inbound:** none — the saga has no inbound HTTP surface.
- **Cross-refs:** ADR-0004 (Checkout Saga Topology), `saga-stuck-runbook.md`.

### `notifications-service`

- **Audience:** `notifications-service`
- **Outbound:** `notifications.commands.send`
  - Pervasive-publisher pattern. Notifications may re-publish retries or test notifications via its own outbox.
- **Inbound:** `notifications.commands.send`
  - All `SendEmailNotificationCommand` messages on `notification.commands` carry this scope; the notifications consumer middleware validates it before dispatching to the handler.
- **Cross-refs:** `events-catalog.md §2` (notifications), `notification.commands` topic in compose.

### `bff`

- **Audience:** `bff` (vestigial — BFF validates user JWTs, not service-auth)
- **Outbound:** 7 scopes — every cross-BC read + `basket.write`:
  - `catalog.read`, `basket.read`, `basket.write`, `ordering.read`, `inventory.read`, `invoicing.read`, `notifications.commands.send`
  - The BFF is the sole HTTP caller of the six BCs in the system.
- **Inbound:** none — user-facing only; inbound requests carry user JWTs (validated against `dotnetatlas` realm user-auth, not service-auth).
- **Cross-refs:** `bff.md §3.1–3.4`.

---

## 3. Cross-reference: command-topic ↔ publisher ↔ consumer

| Topic | Publisher client | Required publisher scope(s) | Consumer client | Consumer must validate |
|---|---|---|---|---|
| `inventory.reservation-commands` | `checkout-saga` | `inventory.commands.reserve` / `.confirm` / `.release` (matching command type) | `inventory-service` | `aud = inventory-service` + scope ∈ { `inventory.commands.reserve`, `inventory.commands.confirm`, `inventory.commands.release` } |
| `ordering.order-commands` | `checkout-saga` | `ordering.commands.create` / `.confirm` / `.cancel` / `.fail` (matching) | `ordering-service` | `aud = ordering-service` + scope ∈ { `ordering.commands.create`, `ordering.commands.confirm`, `ordering.commands.cancel`, `ordering.commands.fail` } |
| `payments.commands` | `checkout-saga` (via `PaymentProcessingSaga`) | `payments.commands.authorize` / `.capture` / `.void` / `.refund` (matching) | `payments-service` | `aud = payments-service` + scope ∈ { `payments.commands.authorize`, `payments.commands.capture`, `payments.commands.void`, `payments.commands.refund` } |
| `notification.commands` | any BC, saga, or notifications-service (all have `notifications.commands.send`) | `notifications.commands.send` | `notifications-service` | `aud = notifications-service` + scope `notifications.commands.send` |

Event topics (e.g., `ordering.orders`, `inventory.stock-events`, `catalog.products`) do NOT require sender auth per ADR-0010 L111 — event consumers are fire-and-forget observers.

---

## 4. Production handoff

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
| `checkout-saga` | `KEYCLOAK__SERVICE_CLIENT_SECRET__CHECKOUT_SAGA` | ″ |
| `notifications-service` | `KEYCLOAK__SERVICE_CLIENT_SECRET__NOTIFICATIONS` | ″ |
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

## 5. Verification

Quick realm-state sanity check (after `docker compose --profile full up -d keycloak`):

```bash
# OIDC discovery
curl -s http://localhost:9011/realms/dotnetatlas/.well-known/openid-configuration \
  | python -c "import sys,json;print(json.load(sys.stdin)['issuer'])"

# Acquire admin token
TOKEN=$(curl -s -X POST http://localhost:9011/realms/master/protocol/openid-connect/token \
  -d 'client_id=admin-cli' -d 'username=admin' -d 'password=admin' -d 'grant_type=password' \
  | python -c "import sys,json;print(json.load(sys.stdin)['access_token'])")

# List all clients — expect 11 realm-declared (plus Keycloak builtins: account, admin-cli, broker, realm-management, security-admin-console)
curl -s "http://localhost:9011/admin/realms/dotnetatlas/clients" \
  -H "Authorization: Bearer $TOKEN" \
  | python -c "import sys,json;cs=json.load(sys.stdin);ours={'e9fdb985-9173-4e01-9d73-ac2d60d1dc8e','dotnetatlas-swagger','catalog-service','basket-service','ordering-service','inventory-service','payments-service','invoicing-service','checkout-saga','notifications-service','bff'};print([c['clientId'] for c in cs if c['clientId'] in ours])"

# List all client scopes — expect the 20 declared scopes plus Keycloak defaults
curl -s "http://localhost:9011/admin/realms/dotnetatlas/client-scopes" \
  -H "Authorization: Bearer $TOKEN" \
  | python -c "import sys,json;ss={s['name'] for s in json.load(sys.stdin)};ours={'catalog.read','catalog.write','basket.read','basket.write','ordering.read','ordering.commands.create','ordering.commands.confirm','ordering.commands.cancel','ordering.commands.fail','inventory.read','inventory.commands.reserve','inventory.commands.confirm','inventory.commands.release','payments.read','payments.commands.authorize','payments.commands.capture','payments.commands.void','payments.commands.refund','invoicing.read','notifications.commands.send'};print('found',len(ours&ss),'of',len(ours));print('missing:',ours-ss)"

# Service-account token for catalog-service with scope catalog.write
curl -s -X POST http://localhost:9011/realms/dotnetatlas/protocol/openid-connect/token \
  -d 'grant_type=client_credentials' \
  -d 'client_id=catalog-service' \
  -d 'client_secret=dev-catalog-service-secret-rotate-in-prod' \
  -d 'scope=catalog.write' \
  | python -c "import sys,json,base64;t=json.load(sys.stdin)['access_token'];p=t.split('.')[1];p+='='*(4-len(p)%4);d=json.loads(base64.urlsafe_b64decode(p));print('azp',d.get('azp'),'aud',d.get('aud'),'scope',d.get('scope'))"
```

Expected result on the last check: `azp catalog-service aud catalog-service scope <...> catalog.write` — the `aud` claim confirms the `audience-self` mapper fired.

---

## 6. Open follow-ups (Wave 0 DoD or later)

1. **`realms/eshop` doc sweep.** Replace `realms/eshop` → `realms/dotnetatlas` and `:8081` → `:9011` in `docs/adr/0010-service-to-service-auth.md:94, 99` and `docs/implementation-prompts/wave-0-platform-prep.md:280`.
2. **Wave 0 M7** — wire the nine `KEYCLOAK__SERVICE_CLIENT_SECRET__*` env vars into compose `environment` blocks and per-service `appsettings.*.json` so `ClientCredentialsTokenHandler` (from M3) can acquire tokens at runtime.
3. **No Kafka header token propagation** ([ADR-0010 lines 102-106](../../docs/adr/0010-service-to-service-auth.md:102)) — application-layer `X-Service-Token` is **NOT** implemented in v1 or v2 ("wrong layer regardless of v1/v2" per ADR). Saga-command consumers run on PLAINTEXT in v1; production hardening = broker SASL/OAUTHBEARER + per-service Kafka topic ACLs (see follow-up #4 below). Keycloak realm clients + scopes are still defined so the v2 broker-level ACL pairs are ready (see Section 3 of this matrix).
4. **Broker-level Kafka auth** (ADR-0010 L81) — SASL/OAUTHBEARER is explicitly OUT for v1 (ADR-0009 profile). Production deployments must enable it.
5. **Secret rotation playbook** — formalize the kcadm.sh rotation recipe above into a runbook when production infra is in scope.
