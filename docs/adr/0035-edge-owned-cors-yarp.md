# ADR-0035: Edge-Owned CORS — Browser Traffic Terminates at YARP, SignalR Included

## Status

Accepted (2026-06-10)

## Context

[#316](https://github.com/DavidCapcuch/DotNetAtlas/issues/316) gave Notifications its first browser-facing surface — the in-app bell SignalR hub at `/hubs/v1/notifications` ([ADR-0032](0032-notifications-dispatch-and-channels.md)) — and shipped it **without** a CORS policy. That forced a question the repo had never decided explicitly: **who owns CORS?**

The four then-existing browser-facing BCs (Basket, Catalog, Inventory, Weather) each carried their own `Cors` config + `UseCors(...)` with explicit pinned origins and `AllowCredentials` (the SPA origin `https://app.example.com` in the eShop BCs; localhost dev origins in Weather) — a per-BC convention that grew while the planned **YARP edge** ([eshop-general-plan.md](../eshop-general-plan.md): SSL termination, path routing, rate limiting; not yet built) remained on the roadmap. SignalR sharpens the fork: its negotiate request is CORS-subject, a credentialed SignalR client requires `WithOrigins(...) + AllowCredentials()` (wildcard is rejected), and WebSockets raise the question of whether the hub must bypass the edge.

## Decision Drivers (ranked)

1. **Single origin-policy point** — allowed-origin lists duplicated across 5+ BCs drift; one edge policy cannot.
2. **CORS is not an API security boundary** — it protects *browsers*, not APIs (non-browser clients ignore it entirely); each BC's own JWT validation ([ADR-0010](0010-service-to-service-auth.md)) is the actual gate, so per-BC CORS duplication buys maintenance, not security.
3. **Reference-minimal coherence** — the repo should demonstrate one clear topology, not two overlapping CORS regimes.
4. **Nothing is blocked today** — no SPA exists until the BFF/YARP slice; in-process integration tests are origin-less.

## Considered Options

### Option 1: Edge-owned CORS — all browser traffic (including SignalR) terminates at YARP

BCs ship no CORS. YARP routes `/hubs/{**catch-all}` to Notifications like any other path — WebSocket proxying is a native YARP feature (it forwards the `Upgrade` handshake, then tunnels frames), so the full SignalR lifecycle (negotiate → upgrade → frames) flows through the edge with **no bypass**. The four existing per-BC `Cors` configs become **transitional** and are removed in the YARP slice.

- ✅ One policy point; BCs stay origin-agnostic; matches the already-fixed YARP positioning.
- ❌ Until YARP lands, no cross-origin browser client can reach the bell hub (acceptable: no SPA exists yet).

### Option 2: Continue the per-BC convention — add Notifications CORS mirroring Basket

- ✅ Works today without YARP; consistent with the four existing BCs.
- ❌ Cements a fifth copy of the origin list that the edge makes redundant; per driver 2 it adds no security.

### Option 3: SignalR direct-to-BC bypass (BC CORS for the hub only), everything else through the edge

- ❌ Rejected outright: a second public origin re-creates the exact CORS problem, plus a hole in edge SSL/rate-limiting. WebSockets do not need it (see Option 1).

## Evaluation Matrix

| Driver (ranked) | 1. Edge-owned | 2. Per-BC | 3. Hub bypass |
|-----------------|---------------|-----------|---------------|
| 1. Single policy point | ✅ | ❌ 5+ copies | ❌ split |
| 2. CORS ≠ security boundary | ✅ no duplication | ❌ dup for nothing | ❌ dup + exposure |
| 3. Reference coherence | ✅ one regime | ➖ status quo | ❌ two regimes |
| 4. Blocked today | ➖ bell browser-unreachable until YARP | ✅ | ✅ |

## Decision

We will use **Option 1: edge-owned CORS**. The YARP edge owns the browser origin policy; SignalR proxies through it like all other traffic; BCs ship no CORS. Notifications' bell hub having no CORS is **correct by design**, not a gap.

## Rationale

Drivers 1–3 all point at the edge, and driver 4's cost is zero in practice — there is no browser client until the BFF/YARP slice, at which point the edge exists to carry the policy. The SignalR concern that motivated a bypass dissolves on inspection: YARP proxies WebSockets natively, and the `access_token` query-string lift ([#316](https://github.com/DavidCapcuch/DotNetAtlas/issues/316)) is a browser-vs-WS-handshake limitation that applies identically with or without a proxy — the token rides the query string *through* YARP and each BC still validates its own JWT (zero-trust unchanged).

## Consequences

### Positive

- One place to maintain allowed origins; adding a browser surface to a BC requires no CORS work.
- The remaining per-BC `Cors` configs and their wiring (Basket, Catalog, Inventory — Weather's already gone with the reference service) simplify away when YARP lands.

### Negative

- Until the YARP slice, the bell hub is unreachable from a cross-origin browser (dev tooling and in-process tests are unaffected). Accepted — no SPA exists.

### Risks

- **The YARP slice forgets the CORS obligations.** Mitigation: the slice's definition-of-done must include (a) CORS policy at the edge (explicit origins + `AllowCredentials` — SignalR rejects wildcard-with-credentials), (b) a WS-capable `/hubs/{**catch-all}` route, and (c) removal of the four transitional per-BC CORS configs.

## Implementation Notes

- **SignalR through YARP:** session affinity is required once Notifications runs multi-replica (negotiate + upgrade must land on the same instance) — or WebSockets-only with skip-negotiation; pairs with the [ADR-0016](0016-redis-topology.md) Redis backplane, deferred together. SignalR's 15 s keepalives traverse YARP's streamed proxying without idle-timeout tuning.
- **Transitional inventory (remove in the YARP slice):** the `Cors` appsettings sections in Basket, Catalog, and Inventory; `CorsDependencyInjection` + `*CorsOptions` in `services/Basket`, `services/Catalog`, `services/Inventory`; and the CORS test artifacts (`test/Basket.UnitTests/Api/Common/CorsDependencyInjectionTests.cs` plus the CORS comment reference in `test/Basket.ArchitectureTests/BaseTest.cs`).

## Related Decisions

- [ADR-0010: Service-to-Service Auth](0010-service-to-service-auth.md) — per-BC JWT validation is the security boundary CORS is not.
- [ADR-0016: Redis Topology](0016-redis-topology.md) — SignalR backplane, the other half of the multi-instance seam.
- [ADR-0032: Notifications Dispatch & Channels](0032-notifications-dispatch-and-channels.md) — the bell hub this decision unblocks.
