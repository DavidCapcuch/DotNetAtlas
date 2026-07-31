# ADR-0024: Domain-event dispatch belongs in the persistence boundary

## Status

Accepted (2026-05-31)

## Context

`Platform.SharedKernel.Base.AggregateRoot<TId>` raises in-process domain events
via `AddDomainEvent(...)`. Events are popped from the aggregate's `_domainEvents`
queue via `PopDomainEvents()` and dispatched through
`Platform.SharedKernel.Base.DomainEvents.IDomainEventDispatcher` to one or more
`IDomainEventHandler<T>` registrations — including the **outbox publisher**
handlers (`*OutboxPublisherDomainEventHandler`) that write Avro envelopes to the
EF-Core-backed outbox table.

Reliable messaging requires those outbox rows to commit **in the same database
transaction** as the aggregate row that raised the event. If the aggregate
commits but the outbox row does not (or vice versa), the system loses the
at-least-once guarantee Kafka-bound consumers rely on (per
[conventions.md § 6](../bc-design/conventions.md)).

For EF-Core-backed BCs, the question is **where in the request flow** the
dispatch happens. Three plausible call sites:

1. **A `SaveChangesInterceptor`** registered on the BC's `DbContext`. The
   interceptor's `SavingChangesAsync` fires after EF has built its change-set
   but before the SQL commits — so events dispatched there can still add
   tracked entities (outbox rows) that EF flushes in the same transaction.
2. **The command handler itself** — pop events off the aggregate and call
   `IDomainEventDispatcher.DispatchAsync` immediately before
   `_outbox.SaveChangesAsync(...)`.
3. **A custom repository** — the repository's `SaveAsync` method internally
   pops events, dispatches them, and calls `SaveChangesAsync`. Used when the
   persistence model is **not** EF-Core (Basket: Redis) or requires the
   dispatch to happen at a specific point inside an explicit append cycle
   (Inventory: event sourcing — see below).

`services/Payments` accumulated the option 2 anti-pattern across four command
handlers (`AuthorizePaymentCommandHandler`, `CapturePaymentCommandHandler`,
`VoidPaymentCommandHandler`, `RequestRefundCommandHandler`) while ALSO having
`DispatchDomainEventsInterceptor` wired on `PaymentsDbContext`. The handler's
`foreach (var de in tx.PopDomainEvents()) await _dispatcher.DispatchAsync(de, ct)`
loop emptied the queue, so the interceptor walked `ChangeTracker.Entries<IAggregateRoot>()`
and saw zero events — same end result, but two divergent dispatch paths in the
same BC. This ADR closes that drift and records the canonical placement
across all BCs.

### Ground-truth dispatch wiring (from code)

| BC | Persistence model | Dispatch site | Why |
|---|---|---|---|
| **Catalog** | EF Core + Postgres | `Catalog.Infrastructure.Persistence.Database.Interceptors.DispatchDomainEventsInterceptor` | Standard EF SaveChanges hook |
| **Ordering** | EF Core + Postgres | `Ordering.Infrastructure.Persistence.Database.Interceptors.DispatchDomainEventsInterceptor` | Standard EF SaveChanges hook |
| **Invoicing** | EF Core + Postgres | `Invoicing.Infrastructure.Persistence.Database.Interceptors.DispatchDomainEventsInterceptor` | Standard EF SaveChanges hook |
| **Payments** | EF Core + Postgres | `Payments.Infrastructure.Persistence.Database.Interceptors.DispatchDomainEventsInterceptor` | Standard EF SaveChanges hook |
| **Inventory** | Event-sourced (EF Core only as the event-row + projection store) | `Inventory.Infrastructure.Persistence.EventStore.EventStoreRepository.AppendAsync` | Dispatch must precede `SaveChangesAsync` so projection-handler and outbox-publisher DbSet writes are queued for the same transaction (see [ADR-0006](0006-event-sourcing-for-inventory.md)) |
| **Basket** | Redis (primary), no `DbContext` | `Basket.Application.Baskets.*` command handlers (post `_repository.SaveAsync`) | No `SaveChangesAsync` to intercept; Redis-backed aggregate per [ADR-0016](0016-redis-topology.md). Trade-off accepted in [ADR-0003](0003-basket-as-technical-bc.md): dispatch is non-atomic with the Redis commit |

## Decision Drivers (ranked)

1. **Atomic outbox writes.** Outbox rows and the aggregate row that raised the
   event must commit (or fail) together. The dispatch site dictates which
   handlers run inside vs outside the persistence transaction.
2. **Single canonical dispatch path per BC.** Two dispatch sites in the same
   BC (handler loop + interceptor) is a foot-gun: a future cross-cutting
   concern added to the interceptor (auditing, tracing, an outbox-routing
   header) silently skips the handler-dispatched path.
3. **Don't make command handlers know about messaging.** A command handler's
   job is to load the aggregate, drive the transition, and persist. Dispatch
   is an infrastructure concern; injecting `IDomainEventDispatcher` into the
   handler couples application code to dispatch wiring.
4. **Symmetry across EF-backed BCs.** Catalog, Ordering, Invoicing already
   converged on the interceptor pattern. Drift in any one BC raises the cost
   of cross-BC reasoning (where do events go in this codebase? "It depends").
5. **Honour the exceptions where they exist for real architectural reasons.**
   Basket (Redis) and Inventory (event sourcing) have different persistence
   shapes; forcing them into the interceptor pattern would either be
   impossible (no `SaveChangesAsync` for Basket) or break the atomicity
   guarantee (Inventory's dispatched handlers need to add tracked entities
   BEFORE EF starts its save cycle).

## Considered Options

### Option 1: `SaveChangesInterceptor` for EF-backed BCs; repository-owned dispatch for the others (chosen)

For every EF-Core-backed BC, register a `DispatchDomainEventsInterceptor`
on the `DbContext` that walks
`ChangeTracker.Entries<IAggregateRoot>()`, pops events, and dispatches them
inside `SavingChangesAsync`. Command handlers never inject
`IDomainEventDispatcher`.

For Basket (Redis), handlers dispatch after `_repository.SaveAsync(...)`
with the accepted non-atomicity per [ADR-0003](0003-basket-as-technical-bc.md).
For Inventory (event-sourced), `EventStoreRepository.AppendAsync` dispatches
events between the event-row append and `SaveChangesAsync` so projection-
handler and outbox-publisher DbSet writes are tracked for the same
transaction.

**Pros:**
- Atomic outbox writes for every EF-backed BC by construction — the
  interceptor runs inside the transaction EF opens for `SaveChangesAsync`.
- Single dispatch path per BC — no risk of two sites diverging on a new
  cross-cutting concern.
- Command handlers stay focused on aggregate orchestration; messaging is
  invisible to them.
- The exceptions (Basket, Inventory) are localized and documented at their
  call sites; future BCs default to the interceptor pattern.

**Cons:**
- The dispatch is "magic" from a handler's perspective — a reader of the
  handler doesn't see the dispatch and must know the interceptor exists.
  Mitigated by this ADR and conventions.md § 6.

### Option 2: Command handlers inject `IDomainEventDispatcher` and dispatch explicitly

Handlers pop events off the aggregate and call `DispatchAsync` immediately
before `SaveChangesAsync`. The interceptor is removed (or never registered).

**Pros:**
- Locally explicit — the dispatch is visible in the handler.
- No "magic" inside `SavingChangesAsync`.

**Cons:**
- Every handler in every EF-backed BC duplicates a foreach loop with no
  business meaning — pure ceremony.
- Easy to forget on a new handler. Doc-only convention; nothing catches
  the omission until a downstream consumer notices a missing message.
- Adding a cross-cutting concern (tracing, auditing) across every dispatch
  requires touching every handler.
- Drove the four-handler Payments anti-pattern that this ADR exists to
  close.

### Option 3: A `BaseRepository<TAgg>` for every BC, dispatching inside `SaveAsync`

Reintroduce hand-rolled repositories (the pattern Payments had pre-ADR-0022)
and centralize dispatch in `BaseRepository.SaveAsync`. Handlers call
`_repo.SaveAsync(aggregate)` instead of `_dbContext.SaveChangesAsync()`.

**Pros:**
- Dispatch is explicit at the repository level, not hidden in an interceptor.

**Cons:**
- Reintroduces the abstraction [ADR-0022](0022-specification-pattern-adoption.md)
  explicitly removed for Payments (`IPaymentRepository`/`PaymentRepository`).
- Forces every BC to grow a repository layer over the `IXxxDbContext` port
  — pure plumbing.
- Doesn't compose well with the existing `ITransactionalOutbox<TDbContext>`
  (`Platform.ReliableMessaging.Outbox.EFCore`), which is the actual
  SaveChanges seam handlers use today.

## Evaluation Matrix

| Driver (ranked) | Option 1: Interceptor + exceptions | Option 2: Handler-injected | Option 3: BaseRepository |
|---|---|---|---|
| 1. Atomic outbox writes | ✅ in-transaction by EF construction | ⚠️ correct today but fragile (depends on dispatch-then-save ordering in every handler) | ✅ in-transaction by construction |
| 2. Single canonical dispatch path | ✅ one per BC | ❌ N handlers × M BCs | ✅ one per BC |
| 3. Handlers free of messaging | ✅ | ❌ | ⚠️ handler calls `_repo.SaveAsync` not `_db.SaveChanges` — different but still messaging-aware indirectly |
| 4. Cross-BC symmetry | ✅ all EF BCs identical | ❌ (and was the drift) | ⚠️ requires resurrecting a layer ADR-0022 removed |
| 5. Honours legitimate exceptions | ✅ | n/a | ⚠️ doesn't help Basket (no DbContext) |

## Decision

We will use **Option 1**:

- **Every EF-Core-backed BC** (Catalog, Ordering, Invoicing, Payments)
  registers a `DispatchDomainEventsInterceptor` on its `DbContext` via
  `optionsBuilder.AddInterceptors(...)`. Command handlers in these BCs
  **MUST NOT** inject `IDomainEventDispatcher`. Domain services likewise
  **MUST NOT** inject `IDomainEventDispatcher` — they orchestrate
  aggregates which raise events via `AggregateRoot.AddDomainEvent`; the
  dispatch still happens at persistence.
- **Inventory** (event-sourced) dispatches inside
  `EventStoreRepository.AppendAsync` between the event-row append and
  `SaveChangesAsync` so dispatched projection/outbox handlers' tracked
  writes commit in the same transaction as the event rows
  (see [ADR-0006](0006-event-sourcing-for-inventory.md)).
- **Basket** (Redis primary) dispatches in the command handler immediately
  after `_repository.SaveAsync(...)`. Non-atomicity with the Redis commit
  is accepted per [ADR-0003](0003-basket-as-technical-bc.md).

## Rationale

**EF Core's interceptor model is the right seam.** `SavingChangesAsync` runs
**inside** the transaction EF Core opens for the save; entities added by
dispatched handlers (the outbox publisher writes
`OutboxMessage` rows via the same scoped `DbContext`) are picked up in the
same flush. The reliable-messaging guarantee is satisfied by construction —
there is no window where the aggregate row commits but the outbox row does
not.

**One canonical path per BC is non-negotiable.** The Payments four-handler
drift made this concrete: the manual `foreach` loop emptied the aggregate's
event queue *before* SaveChanges, so the registered interceptor walked
`ChangeTracker.Entries<IAggregateRoot>()` and dispatched nothing. The
end-to-end behaviour was identical (events still dispatched, outbox rows
still written), but the divergence meant that adding a future cross-cutting
concern (a tracing tag on every dispatch, an audit-log handler that
correlates dispatches to user IDs) to the interceptor would silently skip
the four Payments handlers. This class of bug is invisible to tests until
the consumer-facing behaviour breaks weeks later.

**Handler-side dispatch is a stale habit from pre-interceptor days.** The
`SaveChangesInterceptor` API has been stable since EF Core 3.0 (2019);
treating it as exotic infrastructure is a 2018-era stance. By 2026 the
interceptor pattern is the textbook DDD-with-EF answer (see Vladimir
Khorikov's *Domain Events: Salvation*, Steve Smith's *Clean Architecture*
sample, MediatR-with-EF tutorials). Hand-rolled handler-side dispatch was
the pre-interceptor workaround and survived in this codebase only because
no one removed it.

**The two exceptions are not exceptions to the principle, just to the
mechanism.** In both Basket and Inventory the principle "dispatch is owned
by the persistence boundary, not the application handler" holds — the
persistence boundary just isn't an EF interceptor. In Basket it's a
hand-rolled Redis repository; in Inventory it's the event-store repository.
What stays consistent: command handlers in those BCs invoke ONE persistence
call (`_repository.SaveAsync` / `_eventStore.AppendAsync`) and the dispatch
happens inside that call's transactional envelope.

**`docs/bc-design/conventions.md` § 6 is the index, this ADR is the
authority.** Conventions.md grows a one-line bullet pointing at this ADR;
future contributors looking for "where do domain events go in this
codebase?" find the index entry first, then the ADR for the full picture.
Per ADR-0022's framing for this reference repository, automated
enforcement (a NetArchTest barring `IDomainEventDispatcher` injection in
Application-layer types) is considered overkill — the doc + the
cross-BC pattern is the level of rigor.

## Consequences

### Positive

- Single dispatch path per BC; future cross-cutting concerns added to the
  interceptor flow apply uniformly.
- Application handlers shrink — no ceremonial `foreach` over
  `PopDomainEvents()`. Reads more like the business operation it
  represents.
- New BCs adopting EF Core start from the right pattern: register the
  interceptor, don't inject the dispatcher.
- Future BC-design reviewers can grep for `_dispatcher.DispatchAsync` and
  expect ONLY Basket handlers and Inventory's `EventStoreRepository` to
  hit — any new hit is a regression.

### Negative

- Dispatch is invisible at the handler's call site. Mitigation: ADR-0024
  + conventions.md § 6 + the per-BC `DispatchDomainEventsInterceptor.cs`
  file (one per EF-backed BC) are the discoverable trail.
- A reader unfamiliar with EF Core interceptors must learn one concept
  (the `SavingChangesAsync` seam) to fully understand dispatch flow.
  Acceptable cost; the same reader must already know EF's change-tracking
  model to read any handler.

### Risks

- **Risk:** a future contributor adds `IDomainEventDispatcher` back into a
  command handler "to be safe" or "to add tracing in one place," not
  realising the interceptor already runs. **Mitigation:** ADR-0024 +
  conventions.md cross-reference. Reinforced at code-review time. If
  drift recurs, ADR-0022's reversal — automated NetArchTest enforcement —
  becomes proportionate.
- **Risk:** an EF Core upgrade changes the `SavingChangesAsync`
  semantics. **Mitigation:** EF Core's interceptor contract is part of
  its public stability surface (versioned per the EF Core release notes);
  the unit/integration tests in each BC exercise the dispatch path
  end-to-end and would surface a regression on upgrade.
- **Risk:** Basket / Inventory drift toward each other and lose the
  documented justification. **Mitigation:** the per-BC justifications are
  pinned in this ADR's wiring table; any change to those dispatch sites
  should reference this ADR in the PR description.

## Implementation Notes

### Code changes (this PR)

- **Payments handlers** — remove `IDomainEventDispatcher` injection +
  the manual `foreach (var de in tx.PopDomainEvents()) await
  _dispatcher.DispatchAsync(de, ct)` loop from
  `CapturePaymentCommandHandler`, `VoidPaymentCommandHandler`, and
  `RequestRefundCommandHandler`. Drops the `Platform.SharedKernel.Base.DomainEvents`
  using statement from each.
- **Authorize handler cleanup** — trim the 8-line in-handler WHY comment
  block so that the four Payments handlers are textually parallel and
  the convention lives in this ADR rather than being duplicated at four
  call sites. Keep the H-3 anchor comment (it explains a different,
  local invariant).
- **Test files** — drop `Dispatcher` from each handler's `BuildHandler()`
  call. Replace `Dispatcher.Received().DispatchAsync(Arg.Any<X>(), …)`
  assertions with reads of the aggregate's `PopDomainEvents()` to
  verify the FSM transition raised the expected events. The end-to-end
  dispatch path (interceptor → outbox publisher → outbox row → Kafka)
  stays covered by `test/Payments.IntegrationTests`.
- **`PaymentsHandlerTestBase`** — drop the `Dispatcher` field/property
  and the `Platform.SharedKernel.Base.DomainEvents` using statement
  (no remaining consumers).
- **`docs/bc-design/conventions.md` § 6** — bullet added pointing at this
  ADR.
- **`docs/adr/README.md`** — index row added.

### When to revisit

- If a fifth EF-backed BC appears, default to the interceptor pattern —
  no ADR update needed.
- If a future cross-cutting concern (e.g. an `IDispatchObserver` for
  trace correlation) needs to wrap every dispatch, add it to each BC's
  `DispatchDomainEventsInterceptor` (or factor a shared
  `Platform.Persistence.DomainEvents` base interceptor if the four
  implementations grow common surface). This ADR's premise — dispatch
  is owned by the persistence boundary — stays valid; the implementation
  detail evolves.
- If Basket migrates off Redis or Inventory migrates off event sourcing,
  their dispatch sites should follow the interceptor pattern. The ADRs
  that justify the current exception (ADR-0003 / ADR-0016 for Basket;
  ADR-0006 for Inventory) would be the gating documents.

### Out of scope for this ADR

- The cross-BC `DispatchDomainEventsInterceptor` implementations are
  currently duplicated (one per BC, identical bodies). Factoring a
  shared `Platform.Persistence.DomainEvents.DispatchDomainEventsInterceptor`
  is mechanical and reasonable but independent of this convention
  decision; tracked as a follow-up.
- Automated NetArchTest enforcement of "no `IDomainEventDispatcher` in
  Application-layer command handlers of EF-backed BCs" — see ADR-0022's
  rejection of equivalent automation for the read-side-no-specifications
  rule. Available if doc-only enforcement proves insufficient.

## Related Decisions

- [ADR-0003: Basket as Technical BC](0003-basket-as-technical-bc.md) —
  the Redis-backed exception's home; explains why Basket's dispatch is
  in the handler and not atomic with the Redis save.
- [ADR-0006: Event Sourcing for Inventory](0006-event-sourcing-for-inventory.md) —
  the event-sourced exception's home; explains the `EventStoreRepository`
  append cycle that requires dispatch to precede `SaveChangesAsync`.
- [ADR-0016: Redis Topology](0016-redis-topology.md) — the persistence
  technology choice underlying Basket's exception.
- [ADR-0022: Specification pattern adoption criteria](0022-specification-pattern-adoption.md) —
  prior convention with the same enforcement framing (doc-only, no
  arch-test for the reference repo); this ADR follows the same posture.
- [conventions.md § 6](../bc-design/conventions.md) — cross-cutting
  conventions index entry pointing here.
