# ADR-0011: PII Handling & GDPR Article 17 Path

## Status

Accepted (2026-04-19)

## Context

The eShop reference solution stores PII in several places:

- `ordering.orders` + `ordering.order_items` — buyer name, shipping address, billing address
- `payments.transactions` — buyer id, payment method token (tokenized, not PAN)
- `invoicing.invoices` — buyer name, billing address, VAT data
- External Kafka topics — `ordering.orders`, `payments.transactions`, `invoicing.invoices` carry the same PII in event payloads
- OpenTelemetry traces — span attributes risk leaking PII via default HTTP instrumentation (request bodies, path segments)
- Application logs — similar risk via default ASP.NET logging

Topic retention policies are long: `ordering.orders` is infinite (audit), `invoicing.invoices` is 10 years (EU VAT). Under GDPR Article 17 ("right to erasure"), a buyer may demand deletion of their personal data. If PII is committed to an immutable Kafka topic, naive "delete the row" strategies fail — the topic carries a copy that cannot be mutated.

[ADR-0005](0005-customer-data-in-ordering.md) deferred the redaction path to v2 with "a compensating redaction-tombstone table may be needed in v2". That deferral was appropriate for the design wave but is a known gap. This ADR closes the gap with a concrete strategy; implementation details are realistic for v2 but the contract is defined now so event schemas, DB column choices, and OTEL attribute allowlists don't drift.

The reference solution is deliberately single-region (ADR-0009 profile) and does not target GDPR compliance for real deployment. What it *does* target is teaching the pattern: how do you model erasure in an event-sourced world where topics are immutable?

## Decision Drivers (ranked)

1. **Prevent obvious leakage first** — OTEL span tags and application logs must not carry PII by default. This is a policy that can be enforced today.
2. **Pattern teachable** — the erasure story must be realistic enough that production adopters can take it as a starting template.
3. **No topic mutation** — solutions must respect the append-only contract of event topics. Re-writing history is off the table.
4. **Aggregate-level erasure atomicity** — erasing a buyer must be a single semantic operation across BCs.
5. **Preserve audit legality** — invoicing and payment records are legally required to survive erasure requests for fiscal-law reasons. Erasure applies to personal data; fiscal records stay, but PII within them may be redacted per the applicable rules.

## Considered Options

### Option 1: Crypto-shredding + tombstone events

Every row / event that carries PII has its PII fields stored **encrypted with a per-buyer data encryption key (DEK)**. Keys live in a key store (`buyer_encryption_keys` table / KMS in production). Erasure = delete the buyer's DEK. Past rows / events become unreadable (ciphertext without key).

Also emit a `BuyerErasureRequestedEvent` tombstone so downstream projections can nullify derived PII (BFF read caches, Notifications' subscriber table).

### Option 2: Redaction / null-out on writable tables + accept immutable event data as orphaned

Redact PII in Postgres row-by-row; accept that past Kafka events retain the PII until topic retention expires or manual topic rewrite happens. Works only if the retention horizon is acceptable (it isn't — infinite retention means forever).

### Option 3: No erasure path — document as limitation

Acknowledge the reference solution is not GDPR-compliant. Production adopters must implement erasure themselves.

## Evaluation Matrix

| Driver (ranked) | Option 1: Crypto-shred + tombstone | Option 2: Null-out rows | Option 3: No erasure |
|---|---|---|---|
| 1. Prevent leakage (OTEL/logs) | Needs explicit allowlist (separate mechanism) | Same | Same |
| 2. Pattern teachable | Teaches the industry-standard approach | Teaches a pattern that doesn't work for long retention | Doesn't teach anything |
| 3. No topic mutation | Respects append-only contract | Fails — stale PII in topics | Ignores the problem |
| 4. Aggregate-level atomicity | One DEK delete = instant erasure everywhere | Many per-row updates across BCs | Not applicable |
| 5. Audit legality | Ciphertext remains in fiscal records; "retention of encrypted data" is a defensible posture | Redacted fields may violate fiscal-record requirements | Not applicable |

## Decision

We will use **Option 1: Crypto-shredding + tombstone events + OTEL/log PII allowlist**. V1 implements the allowlist + PII-column inventory; v2 implements the crypto-shredding mechanism. The contract for v2 is defined in this ADR so v1 event schemas and DB columns are compatible with it.

## Rationale

Option 1 is the industry-standard approach for event-sourced + immutable-log systems. Amazon, Stripe, and most EU-regulated SaaS have converged on it because it's the only pattern that genuinely satisfies Article 17 for data already in immutable stores. Option 2 works in traditional OLTP but silently fails in event-sourced or Kafka-retentive systems — teaching it in this reference would teach the wrong lesson. Option 3 is honest but doesn't produce a reusable artifact.

The cost of defining the contract now (column names, event shape, OTEL allowlist) is small; the cost of *not* defining it and then needing to retrofit v1 data when v2 lands is high (every ciphertext column added later means a migration on a huge table).

## Consequences

### Positive

- OTEL span tags and logs are PII-free by policy — enforced by an allowlist middleware today, long before the crypto-shredding implementation lands.
- DB columns carrying PII are marked via a naming convention (`*_enc` for columns that v2 will encrypt), making the PII footprint visible at a glance.
- Kafka event schemas explicitly mark PII fields in Avro `doc` comments, enabling tooling to audit what's on the wire.
- Production adopters get a drop-in v2 implementation path with the contract already matching.
- The fiscal-record tension (invoices must survive 10 years; PII in invoices may not) is resolved cleanly: ciphertext survives, key does not.

### Negative

- v1 does NOT implement crypto-shredding — an erasure request cannot actually be fulfilled in v1. Documented in § Out of scope; production use of v1 code for regulated data is not supported.
- The `*_enc` column naming and Avro `pii` doc comments are conventions, not yet enforcements. Mitigation: architecture test will flag columns in PII-typed tables that don't end in `_enc` (future).
- Key-store outage breaks reads of historical data (since decryption is online). Mitigation: cache DEKs in-process per request with short TTL; production would use a managed KMS with HA.

### Risks

- **Leakage through logs / traces in v1** — before the allowlist middleware is mandatory, developers may log PII ad hoc. Mitigation: peer review + `log-pii-redaction-smoke-test` in the test suite that fails the build if known PII strings leak to serialised log output.
- **Incomplete v2 implementation** — crypto-shredding is subtle (IV handling, key rotation, re-encryption). Mitigation: v2 ADR will supersede this with implementation-level detail.
- **Non-erasable PII** — a buyer's name may appear in free-text fields (support chat notes, etc.). Scope: v1/v2 covers structured fields only.
- **Re-identification via CorrelationId** — CorrelationId is not PII in isolation but can be joined to erased rows. Mitigation: CorrelationId is not an erasure target; it remains after key deletion and leaves an anonymous trail only.

## Implementation Notes

### v1 (lands now — enforceable today)

- **OTEL attribute redaction — OpenTelemetry Collector `attributes` processor** (configured in `src/otel-collector/otelcol-config.yml`):
  - Runs in the collector, not in the .NET process. Batches are scrubbed once per export rather than per span per service — cheaper, language-agnostic, testable in isolation against a config file.
  - Initial deletion list covers the highest-risk keys: `http.request.header.authorization`, `http.request.header.cookie`, `http.response.header.set-cookie`, `url.query`, `user.email`, `user.name`, `buyer.email`, `buyer.name`. Production extends this list as data shapes emerge.
  - For hardened GDPR workloads, a second in-process SDK processor (defense in depth) can be added back — not needed at the reference-solution profile.
  - Forbidden span attributes: raw `buyer.email`, `buyer.address.*`, `payment.method.token`, `customer.name`, raw request bodies.
  - Allowed span attributes (examples of what we want to keep): `http.method`, `http.status_code`, `http.route`, `rpc.service`, `messaging.destination.name`, `messaging.kafka.consumer.group`, `db.system`, `db.name`, `correlation.id`, `order.id`, `payment.id`, `invoice.id`, `buyer.id.hash` (SHA-256 truncated to 16 hex chars).
- **Application logging**:
  - Serilog destructuring policy: `[PII]` attribute on models (in `Platform.SharedKernel`) causes the serialiser to emit `"***"`.
  - `Address`, `PaymentMethodId`, `GatewayTransactionId` VOs are `[PII]`-marked.
- **DB column naming convention**: every column that will carry encrypted PII in v2 is named with `_enc` suffix. V1 stores plaintext in those columns (no crypto yet); the suffix reserves the contract.
  - Examples: `ordering.orders.shipping_address_enc`, `invoicing.invoices.billing_address_enc`, `payments.transactions.payment_method_token_enc`.
- **Avro schema convention**: PII fields carry a custom `pii: true` marker in the Avro `doc` string. Tooling can grep `.avsc` files to audit the PII surface.
- **`BuyerId ≡ JWT sub` enforcement**: `CreateOrderCommand` validator (and every command that accepts a `BuyerId`) MUST verify the submitted buyer id equals the JWT `sub` claim (or admin override). Prevents PII-attribution spoofing.

### v2 (contract defined; implementation deferred)

- **Per-buyer DEK** stored in `platform.buyer_encryption_keys`:
  - `buyer_id uuid PK`, `dek bytea` (wrapped by KEK; production = KMS), `created_at`, `rotated_at?`.
  - Erasure = `DELETE FROM platform.buyer_encryption_keys WHERE buyer_id = ?` (cascading-soft-delete via `erased_at`).
- **Encryption at write**: every `*_enc` column is AES-256-GCM encrypted with the buyer's DEK; IV is per-row and stored with the ciphertext.
- **Decryption at read**: aggregate repositories decrypt on materialisation; handlers never see ciphertext.
- **Kafka event PII**: same scheme — Avro field marked `pii: true` is written as ciphertext using the buyer's DEK; consumer decrypts on inbox processing. Consumer sees `null`-equivalent for events whose DEK has been erased.
- **Tombstone event**: `BuyerErasureRequestedEvent` published to `platform.buyer-erasure` topic → consumed by every BC to nullify derived caches (BFF cache, read models, Notifications subscriber table).
- **Fiscal-record posture**: Invoicing aggregate keeps ciphertext invoices. Erasure renders them unreadable but their existence (invoice number, total, issue date) is preserved per VAT law.

### Architecture tests

- Forbid logging calls with parameters typed `Address`, `PaymentMethodId`, `Email`, `PersonName` — require `[PII]`-destructured logging.
- Forbid `Activity.SetTag("*.address", ...)` / `SetTag("*.email", ...)` / `SetTag("*.pan", ...)` directly — PII keys are deleted at the collector, but emitting them in the first place is still a smell worth blocking at code-review time.
- Every column named `address_*` or `email_*` in a table MUST also end in `_enc` (unless marked with a waiver comment).

### Documentation

- Every BC chapter with PII fields cross-references this ADR in its "PII note" section.
- `pii-and-privacy.md` (to author alongside v2) consolidates column + event inventories.

## Related Decisions

- [ADR-0005: Customer Data in Ordering](0005-customer-data-in-ordering.md) — supersedes its v2-redaction deferral
- [ADR-0008: Correlation-ID Propagation](0008-correlation-id-propagation.md) — CorrelationId exempt from PII allowlist
- [ADR-0009: Reference-Solution Target Profile](0009-reference-solution-target-profile.md) — v1 profile explicitly not GDPR-compliant
- [ADR-0015: Time & Timezone Policy](0015-time-timezone-policy.md) — timestamps remain in plaintext (not PII)
- [ADR-0017: Blob Storage + CDN](0017-blob-storage-cdn.md) — PDF invoices stored in Azurite/Azure Blob are out of scope for v1 encryption; v2 would use SSE-CMK (Azure Storage customer-managed keys)
