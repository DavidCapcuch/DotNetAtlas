# ADR-0014: Feature Flags via OpenFeature with JSON-File Provider

## Status

Accepted (2026-04-19)

## Context

Feature flags are a standard tool in modern services: gradual rollouts, kill switches for misbehaving code paths, A/B tests, environment-specific behavior. The reference solution doesn't currently use them; every path is always-on or always-off at build time.

Teaching feature flags in a reference solution has three sub-problems:

1. **Spec choice** — a vendor-neutral API so code isn't locked to one provider.
2. **Provider choice for v1** — something local, zero-cost, laptop-testable.
3. **Migration path** — adopters must be able to swap to a production SaaS (LaunchDarkly, Split, ConfigCat) without code changes.

[OpenFeature](https://openfeature.dev/) is a CNCF-graduated spec that every mainstream vendor has implemented. Choosing OpenFeature over a vendor-native SDK means the application code stays portable.

## Decision Drivers (ranked)

1. **Vendor-neutral API** — code must not import `LaunchDarkly.Client` or `Split.SDK` directly.
2. **Zero infra for v1** — laptop runs the full solution without external services beyond what docker-compose already provides.
3. **Production migration path** — swap to LaunchDarkly / Split / ConfigCat in production with config change only.
4. **Demonstrates ≥ 3 flag patterns** — gradual rollout, kill switch, topology swap — so readers see what flags are good for.
5. **Observability** — flag evaluations appear in traces + metrics so ops understand which flag is driving behavior.

## Considered Options

### Option 1: OpenFeature + local JSON-file provider

Standard `IFeatureClient` from OpenFeature SDK; provider reads from `appsettings.json` or a mounted file. Hot-reload via `IOptionsMonitor` or file watcher.

### Option 2: OpenFeature + a cloud SaaS (LaunchDarkly, ConfigCat, Split)

Same API, real provider. Requires API keys and external dependency for any demo.

### Option 3: Raw `IConfiguration` + `if (config["Feature:ShowDiscontinued"])`

No library; feature-flag logic inline via plain config.

### Option 4: Microsoft.FeatureManagement (ASP.NET Core native)

Native .NET feature-management library. Tightly integrated with `IConfiguration`.

## Evaluation Matrix

| Driver (ranked) | Option 1: OpenFeature + file | Option 2: OpenFeature + SaaS | Option 3: Raw config | Option 4: Microsoft.FeatureManagement |
|---|---|---|---|---|
| 1. Vendor-neutral | Yes (spec is) | Yes | N/A | .NET-only; LaunchDarkly isn't a native provider |
| 2. Zero infra for v1 | Yes — JSON file in repo | No — needs API keys + SaaS signup | Yes | Yes |
| 3. Production migration | Swap provider in DI | Already production-ready | Rewrite call sites | Swap provider — but narrower ecosystem than OpenFeature |
| 4. ≥ 3 flag patterns | Supports targeting, percentages, variants | Same | Only on/off easily | Supports filters / targets |
| 5. Observability | OpenFeature hooks API emits evaluation events | Same | Manual logging | Filter-based, less standardized |

## Decision

We will use **Option 1: OpenFeature SDK with a local JSON-file provider** in `Platform.ServiceDefaults`, showcasing three flag patterns (gradual rollout, kill switch, topology swap). Production adopters swap the provider via DI — no call-site changes.

## Rationale

OpenFeature is the only vendor-neutral abstraction that has production-grade provider implementations from every major vendor. Writing code against it today means adopters in 2028 can still swap between Split, LaunchDarkly, Statsig, ConfigCat, or self-hosted Flagsmith without touching application code. The JSON-file provider is the correct v1 choice: zero infrastructure, hot-reload via `IOptionsMonitor`, the flag file lives under version control (reference scenarios are reproducible).

Option 2 is what a production adopter would use — but requiring every reader to sign up for a SaaS just to run the reference is a non-starter. Option 3 is where most teams actually start in practice; the reference deliberately *doesn't* teach that path because it doesn't scale (feature-flag semantics drift into a grab-bag of config keys with no evaluation context). Option 4 (Microsoft.FeatureManagement) is reasonable but has a narrower provider ecosystem and less emphasis on evaluation context — OpenFeature bakes in "who is evaluating, what context do they have?" as a first-class concept.

## Consequences

### Positive

- Application code depends only on OpenFeature abstractions.
- Three demonstrable flag patterns in the reference (§ Implementation Notes).
- Swap to a production provider is a DI change, not a code change.
- Evaluation hooks emit OpenTelemetry events — every flag evaluation is observable in Jaeger.
- Evaluation context (`EvaluationContext.TargetingKey = buyerId`) demonstrates per-user flag resolution — the key primitive for gradual rollouts.

### Negative

- One more library in every service (OpenFeature SDK). Negligible.
- Local JSON file must be kept in sync across environments. Mitigation: it's version-controlled; production uses a SaaS provider instead.
- Hot-reload is file-watch-based; on systems where file-watching is flaky (containers), a 30-second fallback poll is used. Acceptable.

### Risks

- **Flag rot** — flags outlive their usefulness and code accumulates dead branches. Mitigation: every flag-introducing PR notes a removal target (e.g., "remove after 2026-07-01"). A monthly "flag cleanup" check is recommended.
- **Config divergence between reference and production** — flag names in JSON must match the SaaS provider's flag names when migrated. Mitigation: document the migration checklist.
- **Unguarded boolean flags in tests** — tests that assume default values may break if flag defaults change. Mitigation: tests set explicit flag values via the in-memory test provider.

## Implementation Notes

### Platform surface

`Platform.ServiceDefaults` extension:

```csharp
public static IServiceCollection AddFeatureFlags(this IServiceCollection services, IConfiguration config)
{
    services.AddOpenFeature()
        .AddInMemoryProvider()  // v1 — replaced with SaaS in prod via config
        .AddFileProvider(config["FeatureFlags:FilePath"] ?? "flags.json")
        .AddHook<OtelEvaluationHook>();  // emits OTel events on every eval

    return services;
}
```

- `flags.json` lives at the repo root and is mounted into each service's container.
- `OtelEvaluationHook` adds `Activity` events: `feature_flag.evaluated` with tags `flag.key`, `flag.value`, `flag.variant`, `evaluation.context.targeting_key`.

### Three showcase flags (v1)

| Flag | Pattern | Where evaluated |
|---|---|---|
| `catalog.show-discontinued-in-search` | **Gradual rollout** — boolean; target by `buyerId` for canary cohorts | search query filter predicate (was originally drafted for the projection handlers — moved to the query side once per-event `*ProjectionDomainEventHandler` classes landed) |
| `bff.home-page-eager-cache-warm` | **Kill switch** — boolean; default-on but flip-off under load | BFF startup / background `IHostedService` |
| `checkout.payment-then-stock` | **Topology swap (A/B pattern)** — boolean; default OFF; demonstrates the alternative step-order without changing ADR-0004 | Checkout saga state machine guard on initial transition |

The topology swap flag is **intentionally kept OFF in v1** — it demonstrates the flag-gated path without shipping an untested topology. The code path exists (with tests); the flag is the safety.

### Evaluation context

Every evaluation includes the `EvaluationContext`:

- `TargetingKey` = `buyerId` (for user-facing flags) or `correlationId` (for workflow-level flags)
- Attributes: `service = <bc-name>`, `environment = Development|Staging|Production`, `featureGate.cohort = <optional>`

### JSON file format

OpenFeature's JSON provider schema (simplified):

```json
{
  "flags": {
    "catalog.show-discontinued-in-search": {
      "state": "ENABLED",
      "variants": { "on": true, "off": false },
      "defaultVariant": "off",
      "targeting": {
        "if": { "op": "percentage", "attribute": "targetingKey", "value": 10 },
        "then": "on"
      }
    },
    "bff.home-page-eager-cache-warm": {
      "state": "ENABLED",
      "variants": { "on": true, "off": false },
      "defaultVariant": "on"
    },
    "checkout.payment-then-stock": {
      "state": "ENABLED",
      "variants": { "on": true, "off": false },
      "defaultVariant": "off"
    }
  }
}
```

### Test support

- `InMemoryFeatureProvider` (provided by OpenFeature .NET SDK) for unit tests: `featureClient.Test.Override("catalog.show-discontinued-in-search", true);`
- Integration tests load a per-test flag file via `WebApplicationFactory.UseSetting("FeatureFlags:FilePath", "test-flags.json")`.

### Migration to production

Production DI swaps the provider:

```csharp
services.AddOpenFeature()
    .AddLaunchDarklyProvider(sdkKey: config["LaunchDarkly:SdkKey"]!)  // or Split / ConfigCat equivalent
    .AddHook<OtelEvaluationHook>();
```

Call sites (`featureClient.GetBooleanValueAsync("catalog.show-discontinued-in-search", false, context, ct)`) remain unchanged.

### Observability

- Every evaluation emits an OTel event as above.
- Metrics: `feature_flag.evaluations.count` (counter) tagged `flag.key`, `outcome=default|targeted|error`.
- Jaeger traces show flag evaluations inline with the span that triggered them.

### Lifecycle discipline

- New flag PR includes: purpose, expected lifespan, removal owner, removal date.
- Quarterly cleanup: flags past removal date are flagged in a CI report.

## Related Decisions

- [ADR-0008: Correlation-ID Propagation](0008-correlation-id-propagation.md) — CorrelationId can be a TargetingKey for workflow-level flags
- [ADR-0009: Reference-Solution Target Profile](0009-reference-solution-target-profile.md) — profile explicitly does not require a SaaS feature-flag provider
- [ADR-0004: Checkout Saga Topology](0004-checkout-saga-topology.md) — `checkout.payment-then-stock` flag demonstrates the alternative topology without changing the decision
