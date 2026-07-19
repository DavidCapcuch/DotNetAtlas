# ADR-0009: Reference-Solution Target Profile

## Status

Accepted (2026-04-19)

## Context

Several design knobs in the eShop reference solution are set without explicit justification: Kafka partition counts (3 for most topics, 6 for `inventory.reservations`), Redis topology (single AZ, AOF persistence), saga timeouts (order 30s / stock 60s / payment 90s / confirm 30s / compensation 300s), reservation TTL (15 min), outbox-relay polling interval, DLT cumulative alert threshold (50/24h). Each looks arbitrary in isolation.

The missing piece is a stated **target profile** — the operational envelope the reference solution is engineered for. Without it, readers can't assess whether partition counts are under- or over-provisioned, whether retries are too aggressive, or whether the absence of cross-AZ replication is a mistake or a deliberate simplification. Architects picking up the solution for a real deployment also can't tell what they need to change.

This ADR pins the profile explicitly so every subsequent design choice can point to it as the shared baseline.

## Decision Drivers (ranked)

1. **Pedagogical honesty** — readers must know this is a reference solution, not a production template. Call out what we deliberately don't do.
2. **Grounded design choices** — partition counts, timeouts, retention windows should all derive from the profile. A reader asking "why 3 partitions?" gets a defensible answer.
3. **Portability to production** — the profile must make clear which knobs to turn when taking the solution to production (scale, HA, multi-region).
4. **Testability on a developer laptop** — the whole solution must run via `docker compose --profile full up -d` on a modern laptop. No cloud dependencies.
5. **Document once; reference everywhere** — one canonical spec, not a line scattered across 20 docs.

## Considered Options

### Option 1: Reference-solution profile (single-AZ, demo-scale, best-effort DR)

Explicitly scoped for learning and integration testing. Targets ≤ 50 rps sustained, p99 ≤ 500 ms end-to-end checkout, single-AZ, single-region, RPO = best-effort (whatever Postgres + Redis AOF give you on crash), RTO = next-business-day (ops restores from backup when they notice).

### Option 2: Production-plausible profile (multi-AZ, SLA-bearing)

Target a realistic small-merchant profile: 500 rps, p99 ≤ 200 ms, multi-AZ Postgres + Redis Sentinel, RPO ≤ 1 min, RTO ≤ 15 min. Would force HA primitives into the reference: replicated Postgres, Redis Sentinel/Cluster, Kafka replication factor 3, multiple Kafka brokers, active-active gateway.

### Option 3: No stated target profile

Leave the profile implicit and let readers infer from code and config.

## Evaluation Matrix

| Driver (ranked) | Option 1: Reference profile | Option 2: Production-plausible | Option 3: Implicit |
|---|---|---|---|
| 1. Pedagogical honesty | Clear: "learning, not production" | Conflates teaching with production readiness | Invites mistakes |
| 2. Grounded design choices | 3 partitions, AOF Redis, 10-min TTLs — all derivable | Partition counts must reflect real throughput (6+); forces complexity | Choices look arbitrary |
| 3. Portability to production | Short "what to change" checklist | Adopters already have production shape | Adopters must re-derive the profile |
| 4. Laptop-testable | Trivially runs on `docker-compose` | HA stack challenging locally (Sentinel, Kafka replication, 3x brokers) | Same as Option 1 |
| 5. Document once | This ADR + one § in master-design | Same | Nothing to point to |

## Decision

We will use **Option 1: Reference-solution profile** — ≤ 50 rps sustained, p99 ≤ 500 ms end-to-end checkout, single-AZ, single-region, RPO best-effort, RTO next-business-day. The profile is prominently stated in `eshop-master-design.md` and referenced from every design choice that derives from it.

## Rationale

The reference solution's primary job is **teaching patterns**, not hosting production traffic. Option 2 bakes HA infrastructure into every reader's first encounter with, say, the saga state machine — and the HA scaffolding obscures the pattern being taught. A reader trying to understand compensation paths doesn't need Redis Sentinel in the way at the same time. Option 3 (implicit) is the current status quo and is why this ADR exists: we watched reviewers independently question partition counts, Redis durability, and compensation timeouts because nothing grounded the numbers.

A reader who later needs production shape gets the best of both: the pattern is isolated from HA complexity, and the "how do I take this to production?" checklist in this ADR gives them the specific knobs to turn. That split (teach the pattern, then scale-up separately) is itself a valuable pedagogical pattern.

## Consequences

### Positive

- Partition counts, timeouts, retention windows, and persistence choices all have a defensible one-line justification: "derived from ADR-0009".
- Contributors know what changes are *out* of scope (geo-replication, multi-region saga state, zero-downtime migrations).
- Laptop-only footprint is preserved; `docker-compose --profile full up -d` remains the demo experience.
- Production-bound adopters get the "what to change" checklist (§ Implementation Notes) as a starting migration guide.

### Negative

- Reference solution cannot demonstrate HA patterns end-to-end. Mitigation: a future v2 "production variant" can layer them in without rewriting the domain.
- Some reviewers may see the profile as under-engineering. The profile statement preempts the criticism by making the scope explicit.
- The numbers in the profile are opinionated (50 rps, 500 ms p99). Different audiences may disagree. They are chosen to fit a laptop; tune them if you fork.

### Risks

- **Silent drift** — a future contributor changes a partition count or timeout without re-evaluating against the profile. Mitigation: every such change must cite this ADR or propose a successor.
- **Misreading as prescriptive** — a reader could assume the profile is what eShop should ship with. The `eshop-master-design.md` header and this ADR both call it out explicitly.

## Implementation Notes

**Target profile (reference solution, v1):**

| Dimension | Target |
|---|---|
| Throughput (sustained) | ≤ 50 rps across all public endpoints |
| End-to-end checkout p99 | ≤ 500 ms (basket → confirmed order) |
| Catalog browse p99 | ≤ 100 ms (cached) / ≤ 300 ms (uncached) |
| Deployment topology | Single AZ, single region, single Postgres, two Redis instances (basket / cache) |
| Availability | Best-effort (single-instance Postgres, single Kafka broker, Redis AOF) |
| RPO | Best-effort — Redis AOF loss window (≤ 1s); Postgres depends on backup cadence (nightly in dev) |
| RTO | Next business day (ops manually restores) |
| Data residency | Single region — no replication, no GDPR data-residency guarantees |
| PII retention | Matches topic retention (see ADR-0011) |
| Scale mechanism | Vertical — bump container resources. Horizontal scale is out of scope for v1. |

**Derivable choices:**

- Kafka partition counts: 3 (baseline) / 6 (inventory.reservations, highest fan-out). Sized for ≤ 50 rps × 5 items/order peak.
- Retention: infinite for audit topics, 30 days Basket, 7 days command topics, 10 years Invoicing (legal, not profile-driven).
- Redis: AOF `everysec` (for Basket); volatile no-persistence (for Cache backplane). Not Sentinel / not Cluster.
- Postgres: single instance, local storage, no read replicas. Schemas isolated per BC to simplify future extraction.
- Saga timeouts: derived from single-hop network latency (≤ 10ms local) + gateway-stub response time (≤ 50ms) + headroom. See `checkout-saga.md § 7`.
- Reservation TTL: 15 min — matches customer checkout abandon rate; not driven by scale.
- **HTTP transport resilience** (retry, circuit-breaker, timeout): owned by the YARP ingress gateway, not by individual services. Services keep `HttpClient` configuration minimal and let the gateway manage backoff / break against the ADR-0009 envelope. This keeps the pattern centralized and avoids double-counting retries between the gateway and per-service handlers. Services retain *business* retries — outbox redelivery, Kafka consumer retry, idempotent command replay — which operate at a different layer than transport. If the gateway is offline in dev and a per-service fallback is needed, `Microsoft.Extensions.Http.Resilience`'s `AddStandardResilienceHandler()` is one line per client; no shared preset class needed.

**"Taking this to production" checklist:**

1. Split Postgres per BC; add read replicas; configure continuous WAL archiving for RPO ≤ 1 min.
2. Redis: Sentinel for basket (HA) or Cluster for scale; same for cache-backplane Redis.
3. Kafka: replication factor 3; min.insync.replicas 2; 3 brokers minimum.
4. Increase partition counts based on actual workload measurement (not pre-emptive).
5. Add cross-AZ Postgres standby with automatic failover.
6. Move Azurite to a real Azure Blob Storage account (Aspire `AddAzureStorage` swaps automatically when not in `RunAsEmulator()` mode) and front with Azure Front Door / Azure CDN; see ADR-0017.
7. Implement GDPR Article 17 tombstone + crypto-shredding per ADR-0011.
8. Add rate-limit breakglass audit sink (file log is v1; real deployments need an audit log system).
9. Schema-compat CI gate (ADR-0007 follow-up) becomes mandatory, not advisory.
10. **Auth (deployed JWT-bearer):** Every inbound-JWT edge — the seven BCs (Catalog, Basket, Ordering, Inventory, Payments, Invoicing, Notifications) and the BFF — **fails closed** in any deployed environment (the published image defaults to `Production`): the host **refuses to start** until *both* of these override the local-dev defaults in the base `appsettings.json`:
    - `Authentication__JwtBearer__RequireHttpsMetadata=true` — base ships `false`.
    - `Authentication__JwtBearer__Authority` → a reachable `https://` OIDC endpoint whose TLS chain the service trusts — base ships `http://localhost`.

    Overriding only one still fails **at startup**: the platform guard trips on `RequireHttpsMetadata=false`, and an `http://` authority under `RequireHttpsMetadata=true` is rejected by the framework's own metadata-address check — both during the `ValidateOnStart` host materialization, so a host that starts has cleared both. Only an *unreachable or untrusted* `https` backchannel surfaces later, on the first authenticated request — so smoke-test a real authenticated request after deploy, not just a boot/health probe. `RequireHttpsMetadata` gates the service→Keycloak metadata/JWKS *backchannel* — not inbound request TLS, which you terminate at your ingress (the planned YARP edge is not yet built; see ADR-0035). The guard lives once in the platform `JwtBearerConfigurator` (`AddPlatformJwtBearer`, wired with `ValidateOnStart`) and applies uniformly to every edge — there is no per-BC guard. An internal-plaintext-metadata topology (Keycloak reachable only over cluster-internal `http://`) is **out of scope**: the guard admits no exception, so an adopter who needs it relaxes the platform guard themselves, accepting the plaintext metadata/JWKS MITM surface.

## Related Decisions

- [ADR-0001](0001-centralized-saga-orchestration.md) — centralized saga placement fits single-AZ profile
- [ADR-0004](0004-checkout-saga-topology.md) — saga timeouts derive from § Implementation Notes above
- [ADR-0006](0006-event-sourcing-for-inventory.md) — ES without snapshots acceptable at this profile; flash-sale hot-aggregate thrash acknowledged
- [ADR-0007](0007-avro-compatibility-modes.md) — infinite retention requires FORWARD_TRANSITIVE; this profile accepts the retention cost
- [ADR-0011](0011-pii-handling-gdpr.md) — retention choices here interact with PII retention law
- [ADR-0016](0016-redis-topology.md) — split Redis reflects "single AZ, acceptable data-loss on Redis wipe"
- [ADR-0017](0017-blob-storage-cdn.md) — Azurite + nginx is the local-AZ analog of Azure Blob + Front Door
