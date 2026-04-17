# Event Sourcing vs CRUD for Audit Requirements

## Document Metadata
- **Research Date**: 2026-04-03
- **Research Depth**: Detailed
- **Status**: IN PROGRESS
- **Target Audience**: DotNetAtlas engineering team
- **Context**: Evaluating event sourcing vs current CRUD + outbox approach for audit requirements

---

## Executive Summary

*[To be completed in Phase 4]*

---

## Table of Contents

1. [Core Architectural Comparison](#1-core-architectural-comparison)
2. [Audit-Specific Patterns](#2-audit-specific-patterns)
3. [Compliance Requirements (SOX, HIPAA, GDPR)](#3-compliance-requirements-sox-hipaa-gdpr)
4. [Hybrid Approaches](#4-hybrid-approaches)
5. [Performance and Complexity Trade-offs](#5-performance-and-complexity-trade-offs)
6. [Industry Guidance and Expert Positions](#6-industry-guidance-and-expert-positions)
7. [Applicability to DotNetAtlas](#7-applicability-to-dotnetatlas)
8. [Recommendations](#8-recommendations)
9. [Knowledge Gaps](#9-knowledge-gaps)
10. [Conflicting Information](#10-conflicting-information)
11. [Source Registry](#11-source-registry)

---

## 1. Core Architectural Comparison

### 1.1 CRUD (Create-Read-Update-Delete)

In the traditional CRUD model, an application reads data from the store, modifies it, and updates the current state with new values using transactions that lock the data [S1][S2]. The system stores only the latest state of each entity.

**Key characteristics for audit purposes:**
- Stores only current state -- previous values are overwritten on UPDATE [S1][S2]
- Requires separate, purpose-built mechanisms to capture history (triggers, CDC, temporal tables, or audit log tables) [S2][S5]
- Write operations require read-modify-write cycles with row-level locking, creating contention under load [S2]
- Schema is optimized for current-state queries; historical queries require additional infrastructure [S2][S5]

**Confidence: High** (3+ independent sources: Microsoft Learn [S2], Martin Fowler [S1], microservices.io [S3])

### 1.2 Event Sourcing

Event sourcing captures all changes to application state as a sequence of events, stored in an append-only event store that serves as the system of record [S1][S2][S3]. Current state is derived by replaying the ordered event stream for an entity (a process called "rehydration") [S2].

**Key characteristics for audit purposes:**
- Every state change is recorded as an immutable event -- nothing is overwritten or deleted [S1][S2][S3]
- The event store is the authoritative data source (system of record) [S2]
- Events capture business intent (e.g., "OrderCanceled") not just state snapshots (e.g., "status changed to cancelled") [S2]
- Current state is materialized by replaying events, typically cached in read-optimized projections [S2]
- Changes are reversed through compensating events, not mutations, preserving full correction history [S2]

**Confidence: High** (3+ independent sources: Martin Fowler [S1], Microsoft Learn [S2], microservices.io [S3], Kurrent.io [S4])

### 1.3 Fundamental Differences for Audit Purposes

| Dimension | CRUD | Event Sourcing |
|-----------|------|---------------|
| **Data stored** | Current state only | Full sequence of all state changes [S1][S2] |
| **History** | Lost on UPDATE/DELETE unless separately captured | Inherent -- every event is preserved [S1][S2][S3] |
| **Audit trail** | Must be bolted on (triggers, CDC, audit tables) | Built-in -- the event log IS the audit trail [S2][S4] |
| **Intent capture** | Typically records "what changed" not "why" | Events encode business intent and reason [S2] |
| **Temporal queries** | Requires additional infrastructure (temporal tables, snapshots) | Native -- replay events to any point in time [S1][S2] |
| **Immutability** | Data is mutable by design | Data is immutable by design [S1][S2][S3] |
| **Correction mechanism** | Direct UPDATE/DELETE | Compensating events (preserving correction history) [S2] |
| **Complexity** | Simple, well-understood | Significant -- changes how you store data, handle concurrency, evolve schemas, and query state [S2] |

Martin Fowler notes that "the fundamental idea of Event Sourcing is that of ensuring every change to the state of an application is captured in an event object" and that this makes it "easy to serialize the events to make an Audit Log" [S1]. Microsoft's Azure Architecture Center explicitly warns: "Event sourcing is a complex pattern that introduces significant trade-offs. [...] For most systems and most parts of a system, traditional data management is sufficient." [S2]

**Confidence: High** (4 independent sources confirm these distinctions)

---

## 2. Audit-Specific Patterns

### 2.1 Immutable History

Event sourcing provides an inherently immutable history because events are stored in an append-only store and are never modified or deleted [S1][S2][S3]. Corrections are made by appending compensating events rather than altering existing records [S2]. This immutability means that "if a bug produces incorrect events, those events persist in the store. Fixing the bug in application code does not fix the historical events" [S2].

In contrast, CRUD systems are mutable by default. Audit log implementations bolted onto CRUD systems are typically "append-only but not typically immutable" [S4] -- meaning application code or database administrators could potentially alter them unless additional protections are applied. The Kurrent.io (formerly EventStore) team draws an analogy: "events in an audit log are like events written down in history books. It's mostly accurate, but it's an account of what has happened," while event sourcing captures "the irrefutable events themselves. It is the absolute fact." [S4]

**Confidence: High** (4 independent sources: [S1][S2][S3][S4])

### 2.2 Temporal Queries

Event sourcing natively supports temporal queries -- determining the state of an entity at any point in time by replaying events up to that moment [S1][S2][S3]. Fowler states the pattern enables "starting with a blank state and rerunning the events up to a particular time or event" [S1]. Microsoft confirms: event sourcing "provides a 100% reliable audit log of the changes made to a business entity" and "makes it possible to implement temporal queries that determine the state of an entity at any point in time" [S3].

For CRUD systems, SQL Server provides temporal tables (system-versioned tables) as a built-in mechanism for point-in-time queries [S5]. Temporal tables use `FOR SYSTEM_TIME AS OF` syntax to query the state of data at any past moment, and `FOR SYSTEM_TIME BETWEEN` for range queries [S5]. However, temporal tables capture *what* changed at the row level but not *why* it changed or what the user's business intent was [S6].

**Key distinction**: Event sourcing captures semantic business events ("OrderCanceled with reason: customer request"), while temporal tables capture row-level state snapshots ("status column changed from 'active' to 'cancelled' at timestamp T") [S2][S5][S6].

**Confidence: High** (5 independent sources: [S1][S2][S3][S5][S6])

### 2.3 Audit Trail Implementation Patterns

There are four primary patterns for implementing audit trails, ranging from simple to comprehensive:

**Pattern 1: Application-level audit log tables** -- The application writes audit records to a separate table during business operations. Simple to implement but relies on application discipline; bugs or code paths that skip audit logging create gaps [S4][S7].

**Pattern 2: Database-level mechanisms (triggers, temporal tables, CDC)** -- The database engine captures changes transparently without application code changes. SQL Server temporal tables operate with "no performance overhead because temporal tables rely upon log files already created by SQL Server" [S5]. Change Data Capture reads from the transaction log. These capture all changes but only at the data level, not the intent level [S5][S7].

**Pattern 3: Event sourcing** -- Events ARE the data model. The audit trail is 100% complete and reliable because it IS the system of record [S1][S2][S3][S4]. However, this requires a fundamental architectural shift.

**Pattern 4: Hybrid (CRUD + outbox/CDC + event stream)** -- The application uses CRUD for state management but publishes domain events through an outbox pattern or CDC. This captures business intent in the event stream while maintaining CRUD simplicity for current-state operations [S7].

**Confidence: High** (4+ sources confirm pattern taxonomy)

---

## 3. Compliance Requirements (SOX, HIPAA, GDPR)

### 3.1 SOX Compliance

*[Findings pending research]*

### 3.2 HIPAA Compliance

*[Findings pending research]*

### 3.3 GDPR Compliance (Right to Erasure vs Immutability)

*[Findings pending research]*

---

## 4. Hybrid Approaches

### 4.1 CRUD + Audit Log Tables

*[Findings pending research]*

### 4.2 Change Data Capture (CDC)

*[Findings pending research]*

### 4.3 Event-Carried State Transfer

*[Findings pending research]*

### 4.4 CQRS without Full Event Sourcing

*[Findings pending research]*

---

## 5. Performance and Complexity Trade-offs

### 5.1 Storage Costs

*[Findings pending research]*

### 5.2 Query Patterns and Performance

*[Findings pending research]*

### 5.3 Schema Evolution

*[Findings pending research]*

### 5.4 Team Ramp-up and Operational Complexity

*[Findings pending research]*

---

## 6. Industry Guidance and Expert Positions

### 6.1 Martin Fowler

Martin Fowler defines event sourcing as "ensuring every change to the state of an application is captured in an event object, and that these event objects are themselves stored in the sequence they were applied" [S1]. He identifies audit logging as one of the primary benefits, noting it is "easy to serialize the events to make an Audit Log" that serves multiple purposes beyond compliance -- including customer service troubleshooting and production debugging [S1].

Fowler recommends event sourcing when audit trails provide "competitive advantage" and when systems may later need Parallel Models or Retroactive Events [S1]. He explicitly notes event sourcing is NOT warranted when "basic logging satisfies audit requirements" or when "simple CRUD operations suffice without temporal analysis" [S1]. He also cautions that the event-based interface "is not a natural choice" for many developers and that interactions with external non-event-sourced systems require "sophisticated gateways" [S1].

**Confidence: High** (primary source: martinfowler.com [S1])

### 6.2 Greg Young

Greg Young, the originator of the term CQRS and lead architect of EventStore (now Kurrent), advocated for event sourcing particularly in regulated industries [S8][S9]. Young emphasized that "one of the biggest things needed in complex systems is an audit log" and that event sourcing provides "a complete audit trail of business transactions where you can't delete a transaction or change it once it has happened" [S8]. He described append-only, immutable logs as "absolutely brilliant for many things" and "the ideal transactional model" [S8].

Young's experience came from algorithmic trading, where deterministic systems with complete audit logs are regulatory requirements [S8]. He argued that other business domains can benefit from having "the history of business actions as the source of truth" [S8].

**Confidence: Medium** (2 independent sources: InfoQ [S9], Kurrent.io transcript [S8]; many sources cite Young's talks indirectly)

### 6.3 Microsoft Documentation

Microsoft's Azure Architecture Center provides comprehensive guidance on event sourcing, explicitly positioning it as a solution for auditability challenges in CRUD systems [S2]. Key positions:

1. **Not a default choice**: "Event sourcing is a complex pattern that introduces significant trade-offs. [...] For most systems and most parts of a system, traditional data management is sufficient." [S2]
2. **Selective adoption recommended**: "Event sourcing does not have to be an all-or-nothing decision for your entire system. Apply it selectively to the parts of your system that it benefits the most, such as a payment ledger or order-processing pipeline. Use traditional CRUD for parts when the complexity is not justified, such as user profile management or application configuration." [S2]
3. **CQRS complement**: Microsoft recommends combining event sourcing with CQRS for independently scaling reads and writes [S2][S10].
4. **Kafka is not an event store**: "Message brokers such as Apache Kafka typically lack per-entity stream queries and optimistic concurrency. They work well as a distribution layer to fan out events to projections and external consumers, but they are not a substitute for an event store." [S2]
5. **Temporal tables for simpler needs**: Microsoft documents SQL Server temporal tables as a built-in solution for data audit scenarios with lower complexity than full event sourcing [S5].

**Confidence: High** (primary source: learn.microsoft.com [S2][S5][S10])

### 6.4 ThoughtWorks Technology Radar

*[Research pending -- to be gathered in next research cycle]*

---

## 7. Applicability to DotNetAtlas

### 7.1 Current Architecture Assessment

*[Findings pending research]*

### 7.2 Gap Analysis for Audit Requirements

*[Findings pending research]*

---

## 8. Recommendations

*[To be completed in Phase 4]*

---

## 9. Knowledge Gaps

*[To be completed in Phase 3]*

---

## 10. Conflicting Information

*[To be completed in Phase 3]*

---

## 11. Source Registry

| ID | Source | Domain | Reputation | Access Date | Verification |
|----|--------|--------|------------|-------------|--------------|
| *[Sources to be added as research progresses]* | | | | | |

---

### Confidence Rating Scale
- **High**: 3+ independent sources from trusted domains confirm the claim
- **Medium**: 2 independent sources confirm, or 1 authoritative source
- **Low**: Single non-authoritative source, or conflicting information found
