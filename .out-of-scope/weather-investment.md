# Weather Code Investment

This project will not invest in refactoring, consolidating, or otherwise improving code under `src/Weather` (and its sibling `Weather.*` test/contract projects). `src/Weather` is reference scaffolding that predates the current conventions and is **slated for deletion**. It exists as an illustrative template only.

## Why this is out of scope

Per `CLAUDE.md`:

> **`src/Weather` is reference scaffolding, not production code** — it predates the current conventions (e.g. still uses Ardalis.Specification on the read side) and is slated for deletion. Do **not** flag ADR violations, over-fetch, or other issues in `src/Weather`; treat it as an illustrative template only.

Any work that *polishes* Weather — migrating it onto a shared helper, deduplicating its Swagger/auth wiring, aligning it with newer conventions — is effort spent on code that is being removed. The duplication or divergence such work would eliminate disappears entirely when Weather is deleted.

Worse, folding Weather-only concerns into a shared platform helper actively harms the codebase that *outlives* Weather. Weather's Swagger setup carries extras the shared `Platform.Api` helper deliberately does not model — a `SignalRTypesDocumentProcessor`, cookie-based login, custom resource scopes (`AuthScopes.List`), and config-bound `OpenApiInfo` / server-URL binding. Pushing those into the shared helper to accommodate a soon-to-be-deleted consumer leaks throwaway requirements into the abstraction the six real BCs depend on, making it leakier for everyone else.

**Standing exception:** changes required to keep the whole solution building and green (e.g. making Weather's arch-test base compose with a new shared base so the repo still compiles) are fine — that is maintenance, not investment.

## Prior requests

- #281 — "Migrate Weather.Api Swagger to shared Platform.Api OAuth2/PKCE helper". The issue's own recommendation was **not** to migrate (the SignalR / cookie-auth / custom-scope divergence is real and option 1 trades duplication for a leakier shared abstraction). Closed `wontfix`.
