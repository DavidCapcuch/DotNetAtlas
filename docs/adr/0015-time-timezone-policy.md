# ADR-0015: Time & Timezone Policy — `DateTimeOffset` + `timestamptz`

## Status

Accepted (2026-04-19)

## Context

The eShop reference solution's aggregates, events, and projections all record timestamps: `Order.PlacedAt`, `PaymentTransaction.CapturedAtUtc`, `Invoice.IssueDate`, `ReservationExpiresAt`, Kafka message timestamps, DB audit columns, OTel span start/end times. These timestamps cross process boundaries: aggregate → domain event → outbox row → Kafka message (Avro schema) → inbox → consuming aggregate.

Three small-but-high-impact inconsistencies are common in .NET codebases:

1. **`DateTime` with `Kind=Unspecified`** — neither UTC nor local; ambiguous on deserialization.
2. **Direct `DateTime.UtcNow` calls in domain code** — no test seam; impossible to write deterministic tests for anything time-dependent.
3. **Postgres `timestamp` vs `timestamptz`** — the former drops timezone info on write; the latter normalizes to UTC with offset.

A reference solution with event sourcing (Inventory), 10-year invoicing retention, a saga with timeouts, and TTL-driven reservations cannot afford any of these bugs. Nor can it afford to teach them implicitly.

## Decision Drivers (ranked)

1. **Determinism across boundaries** — a timestamp must round-trip through HTTP → DB → Kafka → DB → Kafka → consumer without losing offset / kind.
2. **Testability** — domain code must be able to mock "now" for deterministic unit tests of TTLs, FSM transitions, etc.
3. **Matches .NET + Postgres best practice** — learners should leave with production-correct instincts.
4. **Zero ambiguity at read time** — a reader should never need to ask "is this timestamp UTC?".
5. **Avro compatibility** — events stored long-term must use a well-defined Avro logical type.

## Considered Options

### Option 1: `DateTimeOffset` + `timestamptz` + `TimeProvider` abstraction, Avro `timestamp-millis` (UTC-normalized)

Domain uses `DateTimeOffset` (offset preserved in-process, written with offset to Postgres `timestamptz`). Domain never calls `DateTimeOffset.UtcNow` directly — injects the BCL `System.TimeProvider` (since .NET 8) and calls `TimeProvider.GetUtcNow()`. Tests inject `Microsoft.Extensions.Time.Testing.FakeTimeProvider` for determinism. Avro encodes event timestamps as `{"type": "long", "logicalType": "timestamp-millis"}`, which is UTC-epoch-milliseconds.

### Option 2: `DateTime` with `Kind=Utc` + Postgres `timestamp` + ambient UTC convention

Force `Kind=Utc` everywhere. Stores offsetless UTC in Postgres. Use `DateTime.UtcNow` ambiently.

### Option 3: `Instant` via NodaTime library

Third-party library with rigorous time types. `Instant` for UTC, `ZonedDateTime` for civil time.

### Option 4: Unix epoch `long` everywhere

No DateTime type; every timestamp is a `long` millisecond/microsecond epoch.

## Evaluation Matrix

| Driver (ranked) | Option 1: DateTimeOffset + Clock | Option 2: DateTime Utc | Option 3: NodaTime | Option 4: Epoch long |
|---|---|---|---|---|
| 1. Determinism | Offset preserved; roundtrips cleanly | Offset lost; must be reconstructed | Offset preserved; cleaner semantics | Offset irrelevant — always UTC |
| 2. Testability | `FakeTimeProvider` is first-party BCL tooling | `DateTime.UtcNow` is static; shimming requires `System.Fakes` or hand-written wrappers | NodaTime's `IClock` mockable | Handwritten epoch clock |
| 3. .NET + Postgres best practice | Yes — MS-recommended default for multi-zone data | Common but not best-practice for Postgres (which has `timestamptz`) | Niche; power-user | Not idiomatic in .NET |
| 4. Zero ambiguity | Offset makes it explicit | Relies on convention | Explicit | Explicit but loses human readability |
| 5. Avro compatibility | `timestamp-millis` is standard + matches Kafka's native message-timestamp unit | Same | Convert at boundary | `long` — trivial but loses logical-type tooling |

## Decision

We will use **Option 1: `DateTimeOffset` for in-process timestamps + `timestamptz` in Postgres + the BCL `System.TimeProvider` abstraction (since .NET 8) + Avro `timestamp-millis` (UTC-normalized) for event timestamps**. Architecture tests forbid direct `DateTime.UtcNow` / `DateTimeOffset.Now` / `DateTime.Now` calls in domain code.

## Rationale

`DateTimeOffset` is Microsoft's recommended default for any value that crosses process boundaries — it carries the offset, eliminating the "is this UTC?" ambiguity by construction. Postgres's `timestamptz` is the correct pair on the DB side: it normalizes to UTC on write, returns with offset on read, and is indexable. The combination is idiomatic, standard, and teaches the right instincts.

`TimeProvider` is the small but load-bearing piece. Every domain type that tests time-dependent behavior (reservation TTL, saga timeouts, gap-free sequence year rollover) needs deterministic "now". The BCL ships `TimeProvider.System` in production and `FakeTimeProvider` (in `Microsoft.Extensions.TimeProvider.Testing`) for unit tests. Architecture tests enforce the injection discipline; domain code that calls `DateTimeOffset.UtcNow` fails the build.

**Avro `timestamp-millis` over `timestamp-micros`.** Both are standard Avro logical types annotating a `long`, and both normalize to a UTC instant on the wire — event-log topics are retained indefinitely (`retention.ms=-1`) and the offset at which they were produced is an in-process concern, not a business fact. The precision is the real decision, and it turns on two questions: does sub-millisecond precision carry meaning here, and can every consumer in the pipeline preserve it? For this system both answers are "no", and that makes millis the correct default:

- **It matches the transport.** Kafka's own message timestamp (KIP-32) is an `int64` of milliseconds since the Unix epoch. Using millis for payload timestamps keeps the payload unit identical to the broker / `ConsumerRecord` metadata unit — no silent unit-switching between envelope and body.
- **It matches the narrowest consumer.** Kafka Connect and ksqlDB model timestamps at millisecond precision (Connect's logical type is backed by `java.util.Date`) and *truncate* micros/nanos. Choosing micros plants a silent-corruption trap the day someone adds a JDBC/S3 sink connector; millis is the unit the whole Kafka tooling ecosystem carries without loss.
- **The extra precision is not meaningful here.** These are business events — order placed, payment captured, stock reserved, invoice issued. Intra-millisecond ordering is resolved by event sequence/version and aggregate stream order, never by wall-clock precision.

The honest tradeoff favoring micros: the in-process types carry more than millis — `DateTimeOffset` has 100-ns ticks and Postgres `timestamptz` stores microseconds — so the millis wire contract is the narrowest link in the chain, and a consumer that re-persists a Kafka timestamp into its own `timestamptz` keeps only millisecond precision. The unit to revisit *toward* is micros, and the trigger is concrete: a microsecond-precision analytics sink (Spark, Flink, Iceberg/Parquet — all µs-native by default). Absent such a consumer, micros is precision the pipeline cannot carry end-to-end. And because event topics are `FORWARD_TRANSITIVE` ([ADR-0007](0007-avro-compatibility-modes.md)), moving to micros later is a deliberate breaking change via a new schema subject — never an in-place logical-type edit, which Avro/Registry would accept (both are physically `long`) while silently reinterpreting every historical millisecond value as microseconds (1000× wrong).

Option 2's `DateTime.UtcNow` is a common trap; it silently drops offset and makes tests flaky. Option 3 (NodaTime) is superior in some respects but brings a dependency and a parallel type system most .NET teams have never seen. Option 4 is defensible for extreme-scale systems but hostile to debugging and logs.

## Consequences

### Positive

- Round-trips HTTP → DB → Kafka → DB preserve offset. Read-side projections don't need to "guess UTC".
- `FakeTimeProvider` makes TTL / timeout tests deterministic — reservation-expiry tests can call `timeProvider.Advance(TimeSpan.FromMinutes(16))` without async waits.
- Avro `timestamp-millis` matches Kafka's own message-timestamp unit and the millisecond precision Kafka Connect and ksqlDB support natively — consumers interpret timestamps without custom converters or precision truncation.
- Architecture test catches the most common mistake (`DateTime.UtcNow` in domain) before it lands.
- Logs and traces show offset — operators don't have to mentally convert.

### Negative

- One small discipline: inject `TimeProvider` instead of calling `DateTimeOffset.UtcNow`. Adds a constructor parameter to every time-aware class.
- `DateTimeOffset` ≠ `DateTime` — JSON serializers default to different formats. Mitigation: configured `JsonSerializerOptions` uses ISO 8601 with offset (`2026-04-19T14:30:00+02:00`) everywhere.
- EF Core 8 / 9 needs a `HasColumnType("timestamptz")` convention on `DateTimeOffset` properties. Mitigation: central convention in each DbContext's `OnModelCreating`.

### Risks

- **Mixed timezone display in UI** — the BFF may want to display local-time based on buyer locale. That's a presentation-layer concern; domain timestamps remain UTC-offset. BFF handles localization.
- **Daylight-saving transitions** — CET → CEST rollover. DateTimeOffset handles this correctly because offset is explicit (+01:00 vs +02:00).
- **Sub-millisecond precision loss** — `timestamp-millis` truncates to milliseconds. .NET's `DateTimeOffset` (100-ns ticks) and Postgres `timestamptz` (microseconds) both carry more, so the wire contract is the narrowest link. Acceptable because business/audit events carry no sub-millisecond meaning and intra-millisecond ordering is resolved by event sequence/version, not wall-clock. Revisit toward `timestamp-micros` only if a microsecond-precision analytics sink (Spark/Iceberg/Parquet) is added downstream.

## Implementation Notes

### Time abstraction

We use the BCL `System.TimeProvider` (ships with .NET 8+). Production consumers inject `TimeProvider` via constructor:

```csharp
public sealed class Whatever(TimeProvider timeProvider)
{
    public DateTimeOffset Something() => timeProvider.GetUtcNow();
}
```

`TimeProvider.System` is registered automatically by the Generic Host — no custom DI wiring in `AddServiceDefaults()`. Test projects reference `Microsoft.Extensions.TimeProvider.Testing` (already in `test/Directory.Build.props`) and construct `FakeTimeProvider` directly:

```csharp
var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero));
timeProvider.Advance(TimeSpan.FromMinutes(16));
```

### MassTransit saga scheduler — known seam

`TimeProvider` injection covers application code, but **MassTransit's saga scheduler holds its own clock**. When a saga handler reads `TimeProvider.GetUtcNow()` (replaced by `FakeTimeProvider` in tests) and a test advances `FakeTimeProvider` by 5 minutes, the handler's view of "now" jumps — but messages scheduled by `MessageScheduler` (in-memory or Quartz-backed) still fire at wall-clock time. Tests that exercise scheduled-timeout paths via `FakeTimeProvider.Advance` will appear hung in CI even when their assertion logic is correct.

**Test seam:** saga timeout tests MUST advance time via MassTransit's `ITestHarness` time API (e.g., `harness.TestTimeout` / the in-memory scheduler's built-in advancement primitives), not `FakeTimeProvider.Advance`. Production code in saga handlers may still inject `TimeProvider` for any wall-clock reads it does itself — the seam is exclusively about scheduled messages. Reference example: `saga/SagaOrchestrators.UnitTests/Sagas/PaymentProcessingSagaOrchestratorTests.cs` for the Payments saga; the Checkout saga must follow the same pattern (see [`docs/implementation-prompts/checkout-saga.md` `<verification>`](../implementation-prompts/checkout-saga.md)).

This is the single most common source of flaky saga tests in MassTransit codebases. Architecture tests cannot enforce this seam directly — it's a test-discipline rule, not a static-analysis target.

### Architecture tests

Per-BC `ArchitectureTests` project asserts:

1. Types in `{BC}.Domain` do not reference `DateTime` (all domain timestamps are `DateTimeOffset`).
2. Types in `{BC}.Domain` do not call `DateTimeOffset.UtcNow`, `DateTime.UtcNow`, `DateTime.Now`, `DateTimeOffset.Now` (enforce via a Roslyn analyzer rule or a reflection-based test).
3. Constructors / methods that need "now" accept `TimeProvider` via DI or receive `DateTimeOffset` from the application layer.

### Platform base — `DomainEvent.OccurredOnUtc` is `required` (Wave 1.5 cross-cutting)

`Platform.SharedKernel.Base.DomainEvents.DomainEvent.OccurredOnUtc` is declared `required` with no default initializer. Callers must supply an explicit value sourced from `TimeProvider.GetUtcNow()`; the prior default of `DateTimeOffset.UtcNow` was an in-base violation of the no-wall-clock rule and silently bypassed `FakeTimeProvider` in tests. Pattern in aggregate methods:

```csharp
var utcNow = _timeProvider.GetUtcNow();
AddDomainEvent(new SomethingHappenedDomainEvent
{
    OccurredOnUtc = utcNow,
    // ...payload...
});
```

Compile-time guarantee: every event-construction site without `OccurredOnUtc =` fails the build. The reflection-based regression test lives in `platform/Platform.SharedKernel.UnitTests/Base/DomainEvents/DomainEventTests.cs`.

### EF Core convention

Every DbContext:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetProperties()))
    {
        if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
        {
            property.SetColumnType("timestamptz");
        }
    }
}
```

Applied once per BC's DbContext via a shared base class in `Platform.ReliableMessaging.Outbox.EFCore`.

### Avro schema convention

All new `.avsc` files use:

```json
{"name": "OccurredAtUtc", "type": {"type": "long", "logicalType": "timestamp-millis"}}
```

The conversion is **schema-driven**, not performed by `UniversalSerDes`: that platform library is a thin pass-through over the Confluent `AvroSerializer`/`AvroDeserializer` and contains no timestamp math. The application-layer mapper converts the domain `DateTimeOffset` to a UTC `DateTime` (`occurredOnUtc.UtcDateTime`); Apache.Avro's `timestamp-millis` logical type then encodes that `DateTime` to a `long` of epoch milliseconds. Decoding is the exact inverse — the same schema yields a `DateTime` with `Kind=Utc`, which consumer adapters wrap back into a `DateTimeOffset` at offset 0 (`TimeSpan.Zero`). Consumers needing local time convert at the presentation layer.

### JSON serialization

`System.Text.Json`'s built-in `DateTimeOffset` serializer already emits ISO 8601 with offset (e.g. `"2026-04-19T14:30:00.0000000+02:00"`) and parses the same format on read. No custom converter is registered. Endpoint frameworks (FastEndpoints, MVC, minimal APIs) use the default behavior; outbox payloads round-trip through `DateTimeOffset` directly.

### Logging & tracing

- Serilog renders `DateTimeOffset` in ISO 8601 with offset.
- OTel span timestamps are UTC nanoseconds per OpenTelemetry spec — unchanged.

### DB column audit

Audit that every `*_at` / `*_utc` column in Postgres schemas is `timestamp with time zone`. Integration test scans `information_schema.columns` and fails if any `timestamp without time zone` appears.

## Related Decisions

- [ADR-0008: Correlation-ID Propagation](0008-correlation-id-propagation.md) — correlation columns pair with `*_at` timestamps; both use `timestamptz`
- [ADR-0009: Reference-Solution Target Profile](0009-reference-solution-target-profile.md) — single-region simplifies the timezone story
- [ADR-0006: Event Sourcing for Inventory](0006-event-sourcing-for-inventory.md) — event-stream `OccurredAtUtc` column uses this policy
- [ADR-0007: Avro Schema Compatibility Modes](0007-avro-compatibility-modes.md) — governs how these timestamp fields may evolve (`FORWARD_TRANSITIVE` on event topics); changing an existing field's logical type is a breaking change requiring a new subject
- [ADR-0011: PII Handling & GDPR](0011-pii-handling-gdpr.md) — timestamps are not PII and not encrypted
