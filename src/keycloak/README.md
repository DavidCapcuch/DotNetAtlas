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
| `dotnetatlas-swagger` | Public | Swagger UI PKCE flow for the 6 bounded-context APIs, plus the audience for the Notifications in-app **bell** SignalR hub. No secret shipped to the browser. Audience mappers stamp a multi-valued `aud` (`{basket,catalog,inventory,invoicing,ordering,payments}-service` + `notifications-service`) so one dev login is accepted by every BC API and by the bell hub. |
| `catalog-service`, `basket-service`, `ordering-service`, `bff` | Confidential | Service-account clients (`serviceAccountsEnabled: true`, client-credentials) with a committed dev secret. The three `*-service` ones validate `aud: {bc}-service`; `bff` is caller-only (no self-audience). |
| `inventory-service`, `payments-service`, `invoicing-service` | Confidential | Inbound-only (`serviceAccountsEnabled: false`, no secret); validate `aud: {bc}-service` stamped by the resource client-scope mapper. See [service-scope-matrix.md](service-scope-matrix.md). |

> **On the Swagger token's broad audience.** `dotnetatlas-swagger` stamps *every* browser-facing
> service audience onto *every* token it issues, so a single dev login is accepted by all six BC
> APIs **and** the Notifications bell hub at once — convenient for Try-it-out across services, and
> (since the bell is not a Swagger surface) for pasting the `access_token` into a WebSocket client
> against `/hubs/v1/notifications`. This is a deliberate dev-only widening: a production-grade
> per-call audience would instead come from requesting the specific resource scope (e.g.
> `catalog.read`, whose mapper stamps only `catalog-service`). It is acceptable here only because
> this is a public, browser-only tooling client with no service privileges; never mirror this
> broad-audience pattern onto a real service client.

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
