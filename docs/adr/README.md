# Architecture Decision Records

This directory contains Architecture Decision Records (ADRs) for DotNetAtlas.

## Index

| ADR | Title | Status | Date |
|-----|-------|--------|------|
| [0001](0001-centralized-saga-orchestration.md) | Centralized Saga Orchestration | Accepted | 2026-03-19 |
| [0002](0002-pricing-in-catalog.md) | Pricing inside Catalog (v1) | Proposed | 2026-04-18 |
| [0003](0003-basket-as-technical-bc.md) | Basket as Technical BC | Proposed | 2026-04-18 |
| [0004](0004-checkout-saga-topology.md) | Checkout Saga Topology | Proposed | 2026-04-18 |
| [0005](0005-customer-data-in-ordering.md) | Customer Data in Ordering | Proposed | 2026-04-18 |
| [0006](0006-event-sourcing-for-inventory.md) | Event Sourcing for Inventory | Proposed | 2026-04-18 |
| [0007](0007-avro-compatibility-modes.md) | Avro Schema Compatibility Modes | Accepted | 2026-04-18 |
| [0008](0008-correlation-id-propagation.md) | Correlation-ID Propagation Rule | Deprecated | 2026-04-19 |
| [0009](0009-reference-solution-target-profile.md) | Reference-Solution Target Profile | Accepted | 2026-04-19 |
| [0010](0010-service-to-service-auth.md) | Service-to-Service Auth (OAuth2 Client Credentials) | Accepted | 2026-04-19 |
| [0011](0011-pii-handling-gdpr.md) | PII Handling & GDPR Article 17 Path | Accepted | 2026-04-19 |
| [0012](0012-api-versioning.md) | API Versioning (URL path `/v{major}/`) | Accepted | 2026-04-19 |
| [0013](0013-idempotency-key-http.md) | Idempotency-Key HTTP Pattern | Accepted | 2026-04-19 |
| [0014](0014-feature-flags-openfeature.md) | Feature Flags via OpenFeature | Accepted | 2026-04-19 |
| [0015](0015-time-timezone-policy.md) | Time & Timezone Policy (`DateTimeOffset` + `timestamptz`) | Accepted | 2026-04-19 |
| [0016](0016-redis-topology.md) | Redis Topology (split basket vs cache) | Accepted | 2026-04-19 |
| [0017](0017-blob-storage-cdn.md) | Blob Storage + CDN (Azurite + nginx) | Accepted | 2026-04-19 |
| [0018](0018-invoice-numbering.md) | Invoice Numbering (Transactional Gap-Free Allocator) | Accepted | 2026-04-19 |
| [0019](0019-pdf-generation-questpdf.md) | PDF Generation Library (QuestPDF) | Accepted | 2026-04-19 |
| [0020](0020-summary-events.md) | Summary Events for Cross-BC Aggregate Snapshots | Accepted | 2026-05-02 |
| [0021](0021-read-side-no-specifications.md) | Ardalis.Specification forbidden on CQRS read side | Accepted | 2026-05-25 |
| [0022](0022-specification-pattern-adoption.md) | Specification pattern adoption criteria | Accepted | 2026-05-29 |
| [0023](0023-payments-event-vs-command-classification.md) | Payments Event-vs-Command Classification | Accepted | 2026-05-30 |
| [0024](0024-domain-event-dispatch-in-persistence-interceptor.md) | Domain-event dispatch belongs in the persistence boundary | Accepted | 2026-05-31 |
| [0025](0025-kafka-consumer-retry-dlt-policy.md) | Kafka consumer retry & dead-letter policy (classify by failure class) | Accepted | 2026-06-01 |
| [0026](0026-checkout-payment-flow-capture-pivot.md) | Checkout payment flow — defer capture to the pivot, Payments owns terminal events | Accepted | 2026-06-02 |
| [0027](0027-kafka-consumer-cooperative-rebalancing.md) | Kafka consumer partition-assignment strategy — CooperativeSticky for rolling/canary deploys | Accepted | 2026-06-02 |
| [0028](0028-error-code-wire-fields-stay-string.md) | `ErrorCode` wire fields stay `string` (reject Avro enum migration) | Accepted | 2026-06-02 |
| [0029](0029-order-keyed-saga-and-pre-assigned-orderid.md) | Order-Keyed Saga & Pre-Assigned OrderId | Accepted | 2026-06-03 |
| [0030](0030-removed.md) | Removed — kept to avoid a numbering gap | Removed | — |
| [0031](0031-notify-user-command-and-notification-id.md) | Channel-Agnostic NotifyUserCommand & Producer-Assigned NotificationId | Accepted | 2026-06-03 |
| [0032](0032-notifications-dispatch-and-channels.md) | Notifications v2 Dispatch & Channels (Hangfire fan-out, at-least-once ledger, SignalR bell) | Accepted | 2026-06-03 |
| [0033](0033-kafka-topic-contract-doc-ssot.md) | SSOT for Kafka topic & event-contract documentation | Accepted | 2026-06-04 |
| [0034](0034-inventory-stock-availability-read-path.md) | Inventory stock-availability read path — Inventory-owned read-through cache, oversell-safe via ES | Accepted | 2026-06-05 |
| [0035](0035-edge-owned-cors-yarp.md) | Edge-owned CORS — browser traffic terminates at YARP, SignalR included | Accepted | 2026-06-10 |

## Creating a New ADR

1. Copy `template.md` to `NNNN-title-with-dashes.md`
2. Fill in the template
3. Submit PR for review
4. Update this index after approval

## ADR Status

- **Proposed**: Under discussion
- **Accepted**: Decision made, implementing
- **Deprecated**: No longer relevant
- **Superseded**: Replaced by another ADR
- **Rejected**: Considered but not adopted
