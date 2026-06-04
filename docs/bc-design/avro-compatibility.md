# Avro Schema Compatibility Modes — retired

> **Retired 2026-06-04 ([ADR-0033](../adr/0033-kafka-topic-contract-doc-ssot.md)).** This doc predated [ADR-0007](../adr/0007-avro-compatibility-modes.md) (its own header used to say "ADR-0007 will be authored… supersedes any conflicting clause") and became almost entirely duplicative once that ADR was Accepted. Its per-subject compatibility table was the single worst per-topic-compat duplication source — and that table was never an independent fact: **compatibility mode is *derived*** from the `.avsc` filename suffix → topic class → mode, machine-enforced by `schema-registry-init` ([ADR-0007](../adr/0007-avro-compatibility-modes.md)). It is therefore not tabulated anywhere.

This file is kept only as a redirect so existing links don't break. Where the content lives now:

| What you came here for | Canonical home |
|---|---|
| Policy + decision (the 7 modes, per-class choice, subject-name strategy, breaking-change process, registry bootstrap loop) | [ADR-0007 — Avro Schema Compatibility Modes](../adr/0007-avro-compatibility-modes.md) |
| Which class → which mode (the derivation rule) | [kafka-topology.md — Topic classes](../kafka-topology.md) |
| Evolution anti-patterns (namespace, aliases, union-widening, defaults, mixed modes) | [ADR-0007 § Implementation Notes](../adr/0007-avro-compatibility-modes.md) |
| CI / CD compatibility-gate pattern | [deployment/schema-compat-checks.md](../deployment/schema-compat-checks.md) |
| Per-event contract (producer / consumers / schema path) | [events-catalog.md § 2](events-catalog.md) |
