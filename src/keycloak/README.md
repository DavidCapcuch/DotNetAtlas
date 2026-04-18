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
| `e9fdb985-9173-4e01-9d73-ac2d60d1dc8e` | Confidential | Backend API OIDC login + token exchange. Authorization Code + PKCE only; ROPC disabled. |
| `dotnetatlas-swagger` | Public | Swagger UI PKCE flow. No secret shipped to the browser. |

ROPC (Resource Owner Password Credentials) is disabled on every client in this
realm. `.http` files under `requests/` use JetBrains' built-in OAuth 2.0 token
helper - the first request against a file that references
`{{$auth.token("keycloak-local")}}` opens Keycloak in the browser, you log in
as `dev@dotnetatlas.com / 123456789`, and the IDE caches the token per user
(outside any committed file) and refreshes silently after that. Config lives in
`requests/http-client.env.json` under `Security.Auth.keycloak-local`.

VS Code REST Client users can configure the same PKCE flow in the extension's
OAuth 2.0 settings pointing at the `dotnetatlas-swagger` client; or log in via
`/swagger` and paste the `access_token` into a custom variable.

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
| Backend client `secret` (committed) | Inject via vault (Keycloak vault provider or env var); rotate per environment. |
| Google IdP `clientSecret` (placeholder) | Real value from a throwaway/dedicated Google OAuth client, injected via env var. |
| `webOrigins`, `redirectUris`, post-logout URIs | Replace the `localhost:5159` / `localhost:7095` entries with the real deployed origins. Never use `"+"` or `"*"`. |
| `bruteForceProtected` (already on) | Keep on. Tune `failureFactor` / `waitIncrementSeconds` per your threat model. |
| Bootstrap admin (`admin` / `admin` via `KC_BOOTSTRAP_ADMIN_*`) | Set to random strong values, then rotate the first admin credential immediately after realm import. |

## Security review reference

See the plan at `~/.claude/plans/can-you-check-the-zany-cake.md` (auth flow
security review) for the full finding list and rationale behind each delta
above.
