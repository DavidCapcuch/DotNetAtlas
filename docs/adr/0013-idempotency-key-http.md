# ADR-0013: Idempotency-Key HTTP Pattern — FastEndpoints + ASP.NET Output Cache

## Status

Accepted (2026-04-19, revised 2026-04-19 to use FastEndpoints native idempotency instead of a custom platform library)

## Context

Client HTTP retries are common: a spotty mobile network, a user double-clicking "Pay now", a retry policy in the BFF's typed HttpClient. Without idempotency, a retried `POST /api/v1/bff/checkout` may create two orders, charge the buyer twice, and produce two invoices. The solution already has Kafka inbox deduplication for async message retries, but HTTP retries are a different flow: the client never sees the inbox, and the handler commits a DB transaction per call.

Stripe popularized the **Idempotency-Key HTTP header** pattern: client supplies a UUID on each state-changing request; server stores `(key, request-hash, response, status)` tuples for a TTL and returns the cached response on replay. The pattern is well-known, well-tested, and orthogonal to Kafka inbox dedup.

The codebase standardizes on **FastEndpoints 7.0.1** (`Directory.Packages.props:20-24`). FastEndpoints includes built-in idempotency support via the `.Idempotency()` chain method, backed by ASP.NET Core's `IOutputCacheStore` interface. ASP.NET Core ships first-party Redis output-cache store (`Microsoft.AspNetCore.OutputCaching.StackExchangeRedis`) that backs the cache durably. The reference solution already has a dedicated Redis instance (`redis-cache` per [ADR-0016](0016-redis-topology.md)) appropriate for this purpose.

The teaching opportunity is to demonstrate the **idiomatic .NET approach** rather than reinventing it.

## Decision Drivers (ranked)

1. **Correctness on retry** — no double-side-effects under realistic retry conditions.
2. **Idiomatic .NET** — use built-in framework features rather than rolling our own where the framework is sufficient.
3. **Distinct from Kafka inbox dedup** — the reference should demonstrate both patterns where they apply; conflating them teaches poorly.
4. **Low-friction for new endpoints** — adding idempotency to a new endpoint should be a single chain method call.
5. **Teachable scope** — scope to the realistic retry points, not every POST. Over-scope looks like cargo-culting.

## Considered Options

### Option 1: FastEndpoints `.Idempotency()` + `IOutputCacheStore` backed by `redis-cache`

Use FastEndpoints' native `.Idempotency(opts => ...)` chain method on the protected endpoints. Output cache store is `Microsoft.AspNetCore.OutputCaching.StackExchangeRedis` configured against `redis-cache` (the volatile Redis instance from ADR-0016). Cached entries TTL = 24h.

### Option 2: Custom platform library `Platform.Idempotency.EFCore`

Author a new platform library with EF-backed `idempotency_keys` table per BC, custom middleware, custom `[Idempotent]` attribute. *(This was the original ADR-0013 decision; superseded by Option 1 after audit.)*

### Option 3: Client-signed deterministic request IDs (e.g., UUID v5 from the request body)

Client generates a deterministic ID by hashing the request payload. Server dedups by ID without hash verification.

### Option 4: Natural-key idempotency at the domain layer

Each handler checks domain-level natural keys (e.g., "no order exists with this CorrelationId from this buyer in the last 5 min"). No platform mechanism.

## Evaluation Matrix

| Driver (ranked) | Option 1: FastEndpoints + Redis | Option 2: Custom platform lib | Option 3: Deterministic IDs | Option 4: Natural keys |
|---|---|---|---|---|
| 1. Correctness on retry | Strong — request hashing is built-in | Strong — but reinvented | Strong iff client trusted | Weak — per-handler |
| 2. Idiomatic .NET | First-party FastEndpoints + ASP.NET surface | Reinvents what the framework already provides | Off-pattern | Bypasses framework affordances |
| 3. Distinct from inbox dedup | Different storage (Redis cache) and surface (HTTP filter) — clearly distinct | Same | Blurs the line | Mixes concerns |
| 4. Low-friction | One chain call: `.Idempotency()` | Custom attribute + DI registration per BC | Per-client wiring | Per-handler boilerplate |
| 5. Teachable scope | Crisp — three opt-in endpoints | Same scope, more code | Less mainstream | Mixes concerns |

## Decision

We will use **Option 1: FastEndpoints' built-in `.Idempotency()` + ASP.NET Core Output Cache backed by Redis (`redis-cache`)**. Scoped to four endpoints in v1: BFF checkout, Basket add-item, Ordering cancel, Invoicing resend. **No new platform library.**

## Rationale

FastEndpoints is the standardized endpoint framework across the solution. Its native idempotency support handles the entire pattern — header parsing, request-body hashing, response caching, hash-mismatch detection — using ASP.NET Core's `IOutputCacheStore` abstraction underneath. Backing it with a real Redis instance gives the same durability and TTL behavior a custom EF-backed implementation would have, with one one-line DI registration instead of an entire platform library.

Using the framework's native surface is the better teaching choice. A reader picking up the reference will encounter FastEndpoints' `.Idempotency()` documentation while learning the framework anyway; demonstrating its use here is the path of least surprise. A custom `Platform.Idempotency` library would teach readers to roll their own — a non-transferable skill — and would silently displace the more useful framework feature.

The supersession of Option 2 (the original chunk-3 decision) is recorded in this revision rather than as a separate ADR because the change is small (drop a planned library, swap to a chain-method call) and no consumer has yet been built on the rejected design.

## Consequences

### Positive

- Retried POSTs return the original response — no double orders, double charges, double cancellations.
- Zero new platform code. The mechanism is `services.AddOutputCache(...)` + `.Idempotency()` per endpoint.
- Redis-backed store is durable across service restarts (within the cache instance's lifetime).
- Clear separation from Kafka inbox dedup: different storage (Redis vs Postgres), different surface (HTTP filter vs Kafka middleware), different semantics. Both teachable side-by-side.
- New endpoints opt in with a single chain method.
- FastEndpoints handles request-body hashing, hash-mismatch 409 responses, and replay returns automatically.

### Negative

- Coupled to FastEndpoints — services that use raw Minimal API or MVC controllers cannot use this mechanism without writing their own filter. Acceptable: every BC in this solution standardizes on FastEndpoints.
- Cache-instance loss (Redis flush, OOM eviction) means a retried request would re-execute. Mitigation: `redis-cache` uses `allkeys-lru`, so eviction under memory pressure is rare; loss is bounded to the in-flight retry window.
- One more reason to keep `redis-cache` healthy. Already true (BFF caches depend on it).

### Risks

- **TTL set too low or too high** — 24h is Stripe's default and a reasonable mid-point. Retry windows are usually single-digit minutes; 24h gives generous headroom without excessive Redis memory.
- **Race condition on first-request storage** — two simultaneous retries both pass through before the first one writes the response. FastEndpoints handles this via the output-cache store's atomic semantics; the Redis store uses Lua scripts internally for the SET-IF-NOT-EXISTS pattern.
- **Misuse as a transaction primitive** — idempotency keys are NOT a distributed transaction mechanism. Mitigation: docs repeatedly emphasise the scope.
- **Client retries with different bodies** — handled: FastEndpoints includes the request hash in the cache key by default; mismatch returns 409.

## Implementation Notes

### Per-service DI registration

In each protected service's `Program.cs` (or `ApiDependencyInjection.cs`):

```csharp
// Register Redis-backed output cache pointed at redis-cache (ADR-0016)
builder.Services.AddStackExchangeRedisOutputCache(opts =>
{
    opts.Configuration = builder.Configuration.GetConnectionString("Redis:Cache")!;
    opts.InstanceName = $"{ServiceName}:idem:";  // namespaces the cache to this service
});

builder.Services.AddOutputCache();
// FastEndpoints picks up IOutputCacheStore automatically
```

NuGet additions in `Directory.Packages.props`:

- `Microsoft.AspNetCore.OutputCaching.StackExchangeRedis` (no version needed — pinned by .NET 10 SDK)

### Per-endpoint configuration

Each protected endpoint's `Configure()` method:

```csharp
public sealed class CheckoutEndpoint : Endpoint<CheckoutRequest, CheckoutResponse>
{
    public override void Configure()
    {
        Post("/api/v1/bff/checkout");
        Idempotency(opts =>
        {
            opts.HeaderName = "Idempotency-Key";
            opts.CacheDuration = TimeSpan.FromHours(24);
            opts.AdditionalCacheKey = ctx => ctx.User.FindFirst("sub")?.Value ?? "anonymous";
            // Hash-verification + 409 on mismatch is on by default
        });
        // ... auth, rate limit, etc.
    }
}
```

The `AdditionalCacheKey` partitions by buyer so two different buyers using the same idempotency key (low-probability collision) cannot see each other's responses.

### Protected endpoints (v1)

| Endpoint | Why | TTL |
|---|---|---|
| `POST /api/v1/bff/checkout` | Customer double-click on pay | 24h |
| `POST /api/v1/basket/items` | Double-click on "Add to basket" | 24h |
| `POST /api/v1/ordering/orders/{id}/cancel` | Admin double-click | 24h |
| `POST /api/v1/invoicing/invoices/{id}/resend` | Admin retry on SMTP bounce | 24h |

### Header contract

- Required header: `Idempotency-Key`
- Format: UUID v4 or v7 — string
- Missing header on a protected endpoint → FastEndpoints returns 400 with a problem-detail
- Replay with same key + same body hash → cached response returned with `200/201/204` original status
- Replay with same key + different body hash → 409 Conflict, problem-detail "Idempotency-Key reused with different request body"

### Storage layout (Redis)

- Key: `{ServiceName}:idem:{idempotency-key}:{request-hash}:{additional-cache-key}`
- Value: serialized response (status, headers, body)
- TTL: 24h

Redis instance is `redis-cache` (volatile, `allkeys-lru` eviction). Loss of an entry mid-retry-window means a duplicate execution — acceptable given the low probability.

### Testing

- Unit / integration tests per endpoint:
  - Header missing → 400
  - First call → handler runs, response stored
  - Second call same key + same body → cached response, handler NOT re-invoked (verified via call-count metric)
  - Second call same key + different body → 409
  - Concurrent calls same key → only one handler invocation
  - Expired entry → fresh handler invocation
- Functional test: smoke-test all four protected endpoints' idempotency in `WebApplicationFactory`-based suites.

### Distinction from Kafka inbox dedup (for docs)

|  | HTTP Idempotency-Key (this ADR) | Kafka Inbox |
|---|---|---|
| **Mechanism** | FastEndpoints `.Idempotency()` + ASP.NET Output Cache | `Platform.ReliableMessaging.Inbox.EFCore` |
| **Who provides the key** | Client (per request) | Producer (per message, usually MessageId) |
| **Storage** | Redis (`redis-cache`) | Per-service Postgres `inbox_messages` table |
| **TTL** | 24h | Until consumer commits (retention-scoped) |
| **Hash verification** | Yes (request-body hash) | No (MessageId trust) |
| **Scope** | POST/PATCH/DELETE HTTP endpoints | Kafka consumers |
| **Failure mode on mismatch** | 409 Conflict | N/A — by design no mismatches |

Both patterns apply in this solution. Neither subsumes the other.

## Related Decisions

- [ADR-0008: Correlation-ID Propagation Rule](0008-correlation-id-propagation.md) — Idempotency-Key is distinct from CorrelationId; the former dedups a single HTTP attempt, the latter threads a workflow
- [ADR-0009: Reference-Solution Target Profile](0009-reference-solution-target-profile.md) — storage and throughput budgets
- [ADR-0012: API Versioning](0012-api-versioning.md) — idempotency middleware is attached per versioned route group
- [ADR-0010: Service-to-Service Auth](0010-service-to-service-auth.md) — orthogonal; idempotency applies after authentication
- [ADR-0016: Redis Topology](0016-redis-topology.md) — `redis-cache` (volatile, allkeys-lru) is the backing store
