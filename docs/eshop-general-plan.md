# eShop Reference Solution — General Plan

## Context

DotNetAtlas is a .NET 10 microservices reference solution currently built around a Weather domain. The goal is to replace Weather with an **eShop reference solution** that showcases additional DDD/microservice patterns not yet covered: product catalog, shopping basket, inventory management (event sourced), checkout saga, BFF aggregation, and YARP reverse proxy.

Existing services (Payments/Payments, Notifications) are cross-functional and will be **reused**. The Order service will be **evolved** into an eShop Ordering service. All existing platform libraries (Platform.CQRS, Platform.SharedKernel, Platform.ReliableMessaging, etc.) remain unchanged and are consumed by the new services.

---

## Solution Structure

```
DotNetAtlas/
├── src/
│   ├── EShop.BFF/                    # Backend-for-Frontend
│   │   ├── EShop.BFF.Api/            # Aggregation endpoints (product page = catalog + inventory)
│   │   └── EShop.BFF.Infrastructure/ # HTTP clients to internal services, caching
│   │
│   ├── grafana/                      # Existing (unchanged)
│   ├── otel-collector/               # Existing (unchanged)
│   └── prometheus/                   # Existing (unchanged)
│
├── services/
│   ├── Catalog/                      # Product Catalog BC
│   │   ├── Catalog.Api/
│   │   ├── Catalog.Application/
│   │   ├── Catalog.Domain/
│   │   └── Catalog.Infrastructure/
│   │
│   ├── Basket/                       # Shopping Basket BC
│   │   ├── Basket.Api/
│   │   ├── Basket.Application/
│   │   ├── Basket.Domain/
│   │   └── Basket.Infrastructure/
│   │
│   ├── Ordering/                     # Order Lifecycle BC (new — greenfield; former services/Order/ deleted)
│   │   ├── Ordering.Api/             # (rename from Ordering.Api)
│   │   ├── Ordering.Application/
│   │   ├── Ordering.Domain/
│   │   └── Ordering.Infrastructure/
│   │
│   ├── Inventory/                    # Stock Management BC — EVENT SOURCED
│   │   ├── Inventory.Api/
│   │   ├── Inventory.Application/
│   │   ├── Inventory.Domain/
│   │   └── Inventory.Infrastructure/
│   │
│   ├── Payments/                      # Existing — reused (Payments)
│   └── Notifications/                # Existing — reused as-is
│
├── saga/
│   └── SagaOrchestrators/
│       ├── Checkout/                 # NEW: Checkout saga (multi-step)
│       ├── Payments/                  # Existing: Payment processing saga
│       └── (old Order sagas removed/replaced)
│
├── platform/                         # Shared libraries — unchanged
├── test/                             # Mirrors service structure
└── docker-compose.yaml               # YARP added as container, new Kafka topics
```

**YARP**: Not a standalone code project. Configured as either:
- A Docker Compose service (reverse proxy container)
- Or `.AddYarp()` in .NET Aspire app host

YARP handles infrastructure routing (SSL, rate limiting, path-based routing). The BFF handles application-level response aggregation.

---

## Bounded Contexts (High-Level)

Domain models, aggregates, value objects, events, and use cases will be designed thoroughly in a **separate architecture session** using `/nw-design` (ddd-architect + system-designer). Below is the scope and purpose of each BC — the architecture skill will produce the detailed design.

| BC | Purpose | Key Pattern to Showcase | Storage |
|----|---------|------------------------|---------|
| **Catalog** | Product information authority — what you sell, categories, pricing | CQRS read-side projections | SQL (write) + denormalized read model |
| **Basket** | Ephemeral shopping session — what a customer intends to buy | Redis-backed aggregate, Anti-Corruption Layer | Redis (primary) + SQL (fallback) |
| **Ordering** | Order lifecycle — placement through fulfillment | Rich status machine (SmartEnum), state-guarded transitions | SQL |
| **Inventory** | Stock levels and reservations — what's available to sell | **Event Sourcing** with projections (single ES example) | Event store + SQL projections |
| **Payments** | Payment processing (existing, reused) | Saga sub-orchestration | SQL |
| **Notifications** | Communication delivery (existing, reused) | Event-driven consumers | — |

---

## Context Map (Directional — to be formalized by architecture skill)

**Known integration patterns:**
- **Catalog → Basket**: Anti-Corruption Layer — Basket stores product snapshots
- **Basket → Ordering**: Saga — checkout triggers order creation
- **Ordering → Inventory**: Saga — order triggers stock reservation
- **Ordering → Payments**: Saga — order triggers payment processing
- **Inventory → Catalog**: Events — stock changes update product availability
- **Ordering → Notifications**: Events — order status changes trigger notifications
- **BFF → all services**: HTTP aggregation for consumer-facing API

**Checkout Saga** (centralized in `saga/SagaOrchestrators/Checkout/`):
General flow: Basket checkout → Create Order → Reserve Stock → Process Payment → Confirm Order → Notify
Compensation: reverse completed steps on failure (release stock, cancel order, refund)
Follows ADR-0001 (centralized saga orchestration, MassTransit state machine).

**Messaging constraint:** All Kafka events MUST use Avro serialization with the platform Schema Registry project (`Platform.Avro.UniversalSerDes`). No JSON or Protobuf — Avro only, registered in Confluent Schema Registry.

Detailed event contracts, Kafka topics, saga state machine, and compensation paths will be designed by the architecture skill.

---

## New Patterns Showcased (vs Weather)

| Pattern | Service | What It Demonstrates |
|---------|---------|---------------------|
| YARP Reverse Proxy | Infrastructure | Request routing, SSL termination (Docker/Aspire config) |
| BFF Aggregation | EShop.BFF | Cross-service response composition for frontend |
| Event Sourcing | Inventory | Event stream as source of truth + read projections |
| Redis-backed Aggregate | Basket | Ephemeral state with distributed cache (FusionCache) |
| CQRS Read Projections | Catalog | Denormalized search view built from domain events |
| Multi-step Saga | Checkout | Richer orchestration with more compensation paths |
| Product Snapshot ACL | Basket→Catalog | Anti-corruption layer for cross-BC data references |
| Category Hierarchy | Catalog | Tree-structured aggregate with parent-child relationships |
| Rich Status Machine | Ordering | SmartEnum status with guarded state transitions |

---

## Recommended Skills Pipeline

### nWave Workflow (Primary)

The nWave methodology provides the most structured path. Each bounded context is treated as a separate feature track:

**Phase 1 — Foundation (once, before any BC)**
1. `/nw-discover eshop-reference` — document the reference solution goal
2. `/nw-design` → `system-designer` — cross-cutting constraints: Kafka topic naming, saga placement policy, service project template, platform library usage

**Phase 2 — Per bounded context (Catalog FIRST, then Basket → Ordering → Inventory)**
1. `/nw-diverge {bc}` — evaluate approach options before committing
2. `/nw-discuss {bc}` — user stories + acceptance criteria (JTBD analysis)
3. `/nw-design {bc}` → `ddd-architect` — domain model (aggregates, events, context map)
4. `/nw-design {bc}` → `solution-architect` — layer structure per service
5. `/nw-devops {bc}` — service scaffolding, CI extensions, docker-compose additions
6. `/nw-distill {bc}` — BDD executable acceptance scenarios
7. `/nw-roadmap {bc}` — ordered TDD implementation steps
8. `/nw-execute` (step-by-step for Catalog) or `/nw-deliver` (for subsequent BCs once patterns are proven)

**Phase 3 — Checkout Saga (after Ordering + Inventory are stable)**
- `/nw-design` → `system-designer` — saga topology for checkout flow
- Follow same distill → roadmap → execute cycle

### Complementary Skills (use alongside nWave)

| Skill | When | Purpose |
|-------|------|---------|
| `architecture-decision-records` | During DESIGN wave | Document key decisions (ES for Inventory, Redis for Basket) in `docs/adr/` |
| `dotnet-contribution:dotnet-backend-patterns` | During DELIVER wave | .NET-specific implementation patterns (EF Core, DI, async) |
| `backend-development:saga-orchestration` | Checkout saga design | MassTransit state machine patterns |
| `backend-development:event-store-design` | Inventory design | Event sourcing infrastructure choices |
| `backend-development:cqrs-implementation` | Catalog read model | CQRS read projection patterns |
| `superpowers:verification-before-completion` | Before each milestone | Evidence-based completion verification |
| `modularity:design` | After initial services | Validate coupling between bounded contexts |

### Execution Strategy

- **Catalog first** — establishes patterns. Use `/nw-execute` (step-by-step review).
- **Basket, Ordering, Inventory** — follow established patterns. Use `/nw-deliver` (end-to-end).
- **Checkout saga** — after Ordering + Inventory have stable domain models.
- **BFF** — after at least Catalog + Inventory are done (needs services to aggregate).

---

## Infrastructure Changes

- **Docker Compose**: Add YARP container, new Kafka topics (`catalog.*`, `basket.*`, `ordering.*`, `inventory.*`), new outbox relay workers per schema
- **Database**: Shared SQL Server, new schemas: `catalog`, `basket`, `ordering`, `inventory`
- **CI/CD**: New test projects in coverage pipeline, new Docker builds per service
- **Aspire** (optional): `.AddYarp()` for gateway, service discovery for internal HTTP clients

---

## Verification

- Each BC has 4 test projects: UnitTests, IntegrationTests, ArchitectureTests, FunctionalTests
- Architecture tests enforce no cross-BC direct references
- Integration tests use Testcontainers (SQL Server, Redis, Kafka)
- Functional tests use WebApplicationFactory for full HTTP stack
- Checkout saga tested with MassTransit test harness
- `dotnet build -m` and `dotnet format` must pass (CI-enforced)

---

## What This Plan Does NOT Cover (Deferred to Architecture Session)

The following will be designed thoroughly in a separate session using `/nw-design`:

- Aggregate designs (properties, invariants, factory methods)
- Value object definitions
- Domain event contracts and Avro schemas
- Use cases per service (commands, queries)
- Event catalog (which service publishes what, Kafka topics, consumers)
- Service interaction diagrams
- Saga state machine definition
- BFF aggregation endpoint contracts
