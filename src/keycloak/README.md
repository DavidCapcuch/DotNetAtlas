# Keycloak (local dev)

Bootstrapped automatically by `docker compose --profile full up -d`.

- **Admin console:** http://localhost:9011 (user: `admin`, pass: `admin`)
- **Realm:** `dotnetatlas`
- **Issuer / Authority:** http://localhost:9011/realms/dotnetatlas
- **Discovery:** http://localhost:9011/realms/dotnetatlas/.well-known/openid-configuration

## Re-importing the realm

`realm-export.json` is read only on **first** container start (when the DB has
no `dotnetatlas` realm). To pick up changes:

```bash
docker compose --profile full stop keycloak
docker exec -it postgres5433 psql -U postgres -c "DROP DATABASE keycloak;"
docker exec -it postgres5433 psql -U postgres -c "CREATE DATABASE keycloak;"
docker compose --profile full up -d keycloak
```

(Or use the admin console to export / merge diffs manually.)

## Clients

| Client ID | Type | Purpose |
|---|---|---|
| `dotnetatlas-swagger` | Public | Swagger UI PKCE flow for the bounded-context APIs + the in-app **bell** SignalR hub. No secret shipped to the browser. Stamps a multi-valued `aud` **unconditionally** for `bff` (token-exchange subject) + `ordering-service` / `invoicing-service` (role-only admin endpoints) + `notifications-service` (bell hub). Catalog/Inventory/Payments audiences ride the admin's requested optional scope (`catalog.write` / `inventory.write` / `payments.read`); Basket gets none (100% BFF-mediated). See [service-scope-matrix.md](service-scope-matrix.md). |
| `catalog-service`, `basket-service`, `ordering-service`, `bff` | Confidential | Service-account clients (`serviceAccountsEnabled: true`, client-credentials) with a committed dev secret. The three `*-service` ones validate `aud: {bc}-service`; `bff` is caller-only (no self-audience). |
| `inventory-service`, `payments-service`, `invoicing-service` | Confidential | Inbound-only (`serviceAccountsEnabled: false`, no secret); validate `aud: {bc}-service` stamped by the resource client-scope mapper. See [service-scope-matrix.md](service-scope-matrix.md). |

> **On the Swagger token's audiences.** `dotnetatlas-swagger` stamps a BC `aud` **unconditionally** only
> where a human admin reaches the BC with **no scope** to carry it: `ordering-service` / `invoicing-service`
> (the role-only ship/deliver/resend admin endpoints), `notifications-service` (the `[Authorize]`-only bell
> hub), and `bff` (the Standard-Token-Exchange holder constraint). The role+scope BCs — Catalog, Inventory,
> Payments — instead get their audience from the admin's requested optional scope (`catalog.write` /
> `inventory.write` / `payments.read`, whose mapper stamps only that callee), and Basket gets none (it is
> 100% BFF-mediated). This unconditional set is a deliberate dev-only convenience for Try-it-out (incl.
> pasting the `access_token` into a WebSocket client against `/hubs/v1/notifications`, since the bell is not
> a Swagger surface); it is acceptable only because this is a public, browser-only tooling client with no
> service privileges. **Never copy this onto a real SPA/service client** — a production user-facing client
> stamps `notifications-service` only and requests resource scopes per call. See
> [service-scope-matrix.md](service-scope-matrix.md).

ROPC (Resource Owner Password Credentials) is disabled on every client in this
realm. For interactive testing, log in via `/swagger` (PKCE through the
`dotnetatlas-swagger` client) and use the issued `access_token`. VS Code REST
Client users can configure the same PKCE flow in the extension's OAuth 2.0
settings pointing at the `dotnetatlas-swagger` client.

## Seed users

All with password `123456789`:

| Email | Roles |
|---|---|
| admin@dotnetatlas.com | admin |
| dev@dotnetatlas.com | Developer |
| d.capcuch@gmail.com | Developer, admin |
| pleb@dotnetatlas.com | — |

## This realm is for local Docker only

The committed `realm-export.json` is tuned for the clone-and-run reference-app
experience. For shared environments (dev / stage / prod) you MUST diverge as
follows - do not copy-paste this file into a real environment:

| Setting (local value) | Change for shared environments |
|---|---|
| `sslRequired: "none"` | `"external"` or `"all"` |
| Service client `secret`s (committed) | Inject via vault (Keycloak vault provider or env var); rotate per environment. |
| Google IdP `clientSecret` (placeholder) | Real value from a throwaway/dedicated Google OAuth client, injected via env var. |
| `webOrigins`, `redirectUris`, post-logout URIs | Replace the committed `localhost` Swagger callbacks/origins (the per-BC `:5100-5106` / `:8100-8106` ports) with the real deployed origins. Never use `"+"` or `"*"`. |
| `bruteForceProtected` (already on) | Keep on. Tune `failureFactor` / `waitIncrementSeconds` per your threat model. |
| Bootstrap admin (`admin` / `admin` via `KC_BOOTSTRAP_ADMIN_*`) | Set to random strong values, then rotate the first admin credential immediately after realm import. |

## Security review reference

See the plan at `~/.claude/plans/can-you-check-the-zany-cake.md` (auth flow
security review) for the full finding list and rationale behind each delta
above.
