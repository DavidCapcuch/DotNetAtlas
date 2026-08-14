# Framework settings whose default is legacy-driven, not correctness-driven

Research note (2026-07-30). Every default, version and quotation below was fetched from a primary source on that date. Every "current repo state" line was verified by reading the named file at the named line, or by a repo-wide grep that returned zero hits — never inferred.

Where a claim could not be reached in a primary source it is marked **Unverified:** with what was tried.

## What was hunted

A knob whose **default is set by backward compatibility rather than by what is correct**, where official guidance for greenfield code is the opposite of the default. The trigger case was `JsonSerializerOptions.RespectNullableAnnotations` / `RespectRequiredConstructorParameters` — both `false` by default, both documented as "highly recommended" to enable in a new application.

Explicitly **out of scope as already adopted**: those two flags at [`UpstreamJson.cs:34-35`](../../src/EShop.BFF/EShop.BFF.Infrastructure/Clients/Common/UpstreamJson.cs) and [`ProductCatalogHttpAdapter.cs:60-61`](../../services/Basket/Basket.Infrastructure/ExternalServices/Catalog/ProductCatalogHttpAdapter.cs).

## Ranking

Ranked by correctness impact × blast radius.

| # | Setting | Verdict |
|---|---|---|
| 1 | `AllowDuplicateProperties = false` at the inbound HTTP edge | **Adopt** |
| 2 | STJ `Respect*Default` feature switches (repo-wide) | **Adopt-with-caveats** |
| 3 | EF Core owned entity types → complex types for value objects | **Adopt-with-caveats** (needs slicing) |
| 4 | `AllowDuplicateProperties = false` in BFF `UpstreamJson` | **Adopt** |
| 5 | `InvariantGlobalization` | **Skip** (stated reason) |
| 6 | `ValidateOnBuild` in deployed environments | **Skip** (stated reason) |
| 7 | Npgsql `Max Auto Prepare` | **Skip** (vendor does not recommend it) |

Sections §8–§15 are **negative findings** — settings hunted for and found already correct, or found to be outside the category. Several are decision-relevant.

---

## 1. `AllowDuplicateProperties` is `true` at every inbound HTTP edge

**Setting.** `JsonSerializerOptions.AllowDuplicateProperties` (and `JsonDocumentOptions.AllowDuplicateProperties`). Default `true`. Introduced **.NET 10**.

**Official recommendation.** The .NET 10 libraries release note is explicit about *why* the default is wrong rather than merely lax:

> The JSON specification doesn't specify how to handle duplicate properties when deserializing a JSON payload. This can lead to unexpected results and security vulnerabilities.

— [What's new in .NET libraries for .NET 10 § Option to disallow duplicate JSON properties](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/libraries). The default of `true` is confirmed on the [API reference page](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonserializeroptions.allowduplicateproperties?view=net-10.0): *"`true` if duplicate property names are allowed when deserializing JSON objects. The default is `true`."*

**Current repo state — verified.** Set at exactly **one** site in the whole solution: [`ProductCatalogHttpAdapter.cs:62`](../../services/Basket/Basket.Infrastructure/ExternalServices/Catalog/ProductCatalogHttpAdapter.cs).

FastEndpoints' serializer is never configured. All seven `UseFastEndpoints(...)` blocks set only `Errors`, `Versioning` and `Endpoints.RoutePrefix` — none touches `config.Serializer.Options`:

- `services/Basket/Basket.Api/Common/FastEndpointsDependencyInjection.cs:27-39`
- `services/Catalog/Catalog.Api/Common/FastEndpointsDependencyInjection.cs:28`
- `services/Inventory/Inventory.Api/Common/FastEndpointsDependencyInjection.cs:29`
- `services/Invoicing/Invoicing.Api/Common/FastEndpointsDependencyInjection.cs:29`
- `services/Ordering/Ordering.Api/Common/FastEndpointsDependencyInjection.cs:29`
- `services/Payments/Payments.Api/Common/FastEndpointsDependencyInjection.cs:30`
- `src/EShop.BFF/EShop.BFF.Api/Common/FastEndpointsDependencyInjection.cs:29`

`ConfigureHttpJsonOptions` returns zero hits repo-wide. So every request body across 44 endpoint types binds with `AllowDuplicateProperties = true`.

**Why the downstream nets do not catch this.** Validation in this repo lives in the Application layer (50 `AbstractValidator` types; **zero** in any `.Api` project — verified by grep). A validator sees the *bound object*, by which point the duplicate has already collapsed to last-value-wins. `{"quantity": 1, "quantity": 9999}` binds `9999` and every validator downstream sees a well-formed `9999`. There is no layer in this solution that can observe the discarded first value.

**Blast radius.** Behaviour change at the HTTP edge of 7 API projects. A request carrying a duplicate key changes from silently binding the last value to a `JsonException` → 400. No legitimate client emits duplicate keys, so real traffic is unaffected. Build-time cost: nil.

**Why this one is safe where `Strict` is not.** This is the separable member of the `Strict` bundle. Per the [.NET 10 release note](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/libraries), `JsonSerializerOptions.Strict` bundles five things, and two of them — `UnmappedMemberHandling.Disallow` and case-sensitive binding — are actively wrong for a consumer of an independently-deployed upstream, because they convert every field the upstream *adds* into a consumer outage. That reasoning is already written down at `ProductCatalogHttpAdapter.cs:49-56` and `UpstreamJson.cs:10-15`. It does **not** extend to duplicate keys: a duplicate key is never a legitimate additive change. Adopt the flag; do not adopt the preset.

**Recommendation: adopt.** Highest correctness-per-unit-of-risk finding in this sweep.

---

## 2. The two `Respect*` flags are set per-instance, not via the documented feature switch

**Setting.** MSBuild `RuntimeHostConfigurationOption` items `System.Text.Json.Serialization.RespectNullableAnnotationsDefault` and `System.Text.Json.Serialization.RespectRequiredConstructorParametersDefault`. Both default `false`. Introduced **.NET 9**.

**Official recommendation.** Both docs carry the identical, unusually direct sentence:

> The `RespectNullableAnnotationsDefault` API was implemented as an opt-in flag in .NET 9 to avoid breaking existing applications. If you're writing a new application, it's highly recommended that you enable this flag in your code.

— [Respect nullable annotations](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/nullable-annotations) and, with `RespectRequiredConstructorParameters` substituted, [Require properties for deserialization](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/required-properties). Both pages document the switch as an MSBuild item, not just a per-options property.

**Current repo state — verified.** `RuntimeHostConfigurationOption` returns **zero hits** across every `.csproj` and `.props` in the repo. No `runtimeconfig.template.json` exists anywhere. The flags are set only as instance properties at the two known ACL sites.

Every other `JsonSerializerOptions` in the solution therefore runs with both `false`:

| Site | File |
|---|---|
| Feature-flag file loader | `platform/Platform.ServiceDefaults/FeatureFlags/JsonFlagLoader.cs:19` |
| Inventory event-store payloads | `services/Inventory/Inventory.Infrastructure/Persistence/EventStore/StockEventSerializer.cs:17` |
| Outbox message headers | `platform/Platform.ReliableMessaging.Outbox.Core/OutboxMessageHeaderExtensions.cs:20` |
| Invoicing invoice/credit-note handlers | `Invoicing.Application/.../IssueInvoiceCommandHandler.cs:383`, `IssueCreditNoteCommandHandler.cs:265` |
| All 7 FastEndpoints inbound edges | see §1 |

**Blast radius.** Adding two lines to the root [`Directory.Build.props`](../../Directory.Build.props) flips the default for **every** `JsonSerializerOptions` instance in every project — services, platform, saga, BFF, and the test tier. This is the widest-reach change in the sweep and the one most likely to surface latent failures.

**The caveat that makes this "with-caveats" rather than "adopt".** `StockEventSerializer` deserializes **durable event-store rows** — historical `StockItem` events written under today's lax defaults. `RespectNullableAnnotations` rejects a member that is *present but null*. Any historical row containing an explicit `null` for a member now annotated non-nullable becomes unreadable on replay, and event replay is not a code path that can be rolled back by redeploying. The same concern applies in weaker form to outbox header rows in flight during a deploy.

Two mitigations, in order of preference:

1. **Adopt the switch repo-wide, and pin the event-store serializer explicitly** — `StockEventSerializer` opts *out* with `RespectNullableAnnotations = false` and a comment stating that durable historical payloads are not re-validatable. This keeps the good default everywhere and makes the one exception legible.
2. Adopt per-tier, starting with `services/` API projects, leaving `platform/` for a second pass.

A replay of the existing Inventory event-store fixtures under the flag is the cheap way to find out whether the risk is real or theoretical before committing.

**Note on what this flag does *not* do.** Per the same doc, a **missing** property does not throw even for a non-nullable member — only an explicit `null` does. And the flag cannot enforce nullability on top-level types, collection *elements* (`List<string>` and `List<string?>` are indistinguishable at runtime), or generic members. So it is a partial net, not a complete one; `required` remains what catches absence.

**Recommendation: adopt-with-caveats.** Highest leverage, widest blast radius. Needs the event-store question answered first.

---

## 3. Every value object is mapped as an owned entity type; EF Core 10 advises complex types

**Setting.** `ComplexProperty` vs `OwnsOne`/`OwnsMany`. Complex types reached usable parity for this purpose in **EF Core 10** (optional complex types, struct support, JSON mapping, `ExecuteUpdateAsync` support all landed in EF10).

**Official recommendation.** From [What's New in EF Core 10 § Complex and owned entity types](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew):

> These issues - as well as various others - make complex types the better choice for modeling JSON and table splitting, and users already using owned entity types for these are advised to switch to complex types.

The same page names the concrete failure modes, all of which stem from owned types being **entity types with identity and reference semantics**:

- Assigning one owned value to another fails: `customer.BillingAddress = customer.ShippingAddress` then `SaveChangesAsync()` → **ERROR**, "since the same entity type can't be referenced more than once". Complex types copy properties over, "as expected".
- LINQ comparison "does not work as expected, since entity types are compared by their identities; complex types, on the other hand, are compared by their contents".
- "bulk assignment of owned entity types is not supported, whereas complex types fully support `ExecuteUpdateAsync` in EF 10".

**Current repo state — verified.** `ComplexProperty` returns **zero hits** across `services/`, `platform/`, `saga/` and `src/`. Hand-written owned-type mappings: **33 `OwnsOne` + 5 `OwnsMany` = 38 sites across 4 BCs** — Invoicing 13, Ordering 9, Catalog 8, Payments 3. Basket (Redis-backed) and Inventory (event-sourced) have none. Examples at `Catalog.Infrastructure/.../ProductConfiguration.cs:54,67,77,87,97,113` (`Sku`, `Name`, `Description`, `Brand`, `Price`, `Dimensions`) and `CategoryConfiguration.cs:45` (`Path`).

> **Counting note.** An earlier revision of this section reported "110 `OwnsOne` + 18 `OwnsMany`" across 6 BCs. That count was wrong: it included tool-generated `*.Designer.cs` and `*ModelSnapshot.cs` files under `Persistence/Database/Migrations/`, which restate every mapping and are regenerated by `dotnet ef`. Excluding generated files gives the 38 figure above. Any grep over this repo's mapping surface must exclude `/Migrations/`.

**Index blocker — EF Core 11, not 10.** Keys and indexes on complex-type properties are an **EF Core 11** feature ([dotnet/efcore#31246](https://github.com/dotnet/efcore/issues/31246), milestone 11.0.0; [What's New in EF Core 11 § Keys and indexes on complex type properties](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-11.0/whatsnew)). Two VOs declare an index inside their owned block and are therefore blocked on EF10: `Product.Sku` (unique `ux_products_sku`) and `Category.Path` (`ix_categories_path`). EF11 also states its complex-type work exists specifically "to unblock using complex types as an alternative to the owned entity mapping approach" — but EF11 "is currently in development" and requires the .NET 11 SDK/runtime.

**Why this matters more here than in a generic app.** `CLAUDE.md` states the codebase follows DDD, and ADR-0036 governs shared-kernel value objects (`Money`, `Address`). Every one of those is a **value object** — an object defined entirely by its attributes, with no identity. EF's owned-entity mapping gives them reference semantics and a hidden identity, which is precisely the wrong model. The three failure modes above are not edge cases for a DDD codebase; they are the operations you would expect a value object to support. Complex types are, by EF's own framing, the value-semantics mapping.

**Boundary of the category — stated honestly.** This is not a boolean flag with a legacy default. It is included because the *driver* is identical: owned-entity mapping is the pre-EF8 path retained for backward compatibility, complex types are the correctness-driven replacement, and EF's guidance for anyone in the repo's position is unambiguous. Judge it on that basis, not as a strict category match.

**Blast radius.** Large. 128 mapping sites across 6 BCs, each touching an `IEntityTypeConfiguration`, potentially a migration, and the persistence tests that pin column names. Constraints that must be designed around, all quoted from the EF10 page:

- "optional complex types currently require at least one required property to be defined on the complex type"
- "collections of structs aren't currently supported"
- `OwnsMany` → complex-type collections require JSON mapping, not table splitting

**Unverified:** Npgsql provider support for complex-type JSON mapping in EF10 was not checked. The EF10 page's JSON examples are SQL Server 2025-specific; it says only "For support with other databases, consult the documentation for your EF provider." This must be settled before any `OwnsMany` site is touched — it may bound the migration to `OwnsOne` sites only.

**Recommendation: adopt-with-caveats, and slice it.** Too large for one ticket and it has a genuine design fork (which VOs move, whether `OwnsMany` moves at all, whether column names are preserved to avoid migrations). Run `daca-grill-to-tickets` rather than filing a single vague ticket.

---

## 4. BFF `UpstreamJson` is missing the third flag its sibling has

**Setting.** Same as §1, at a site that already adopted the other two.

**Current repo state — verified.** [`UpstreamJson.cs:32-36`](../../src/EShop.BFF/EShop.BFF.Infrastructure/Clients/Common/UpstreamJson.cs) sets `RespectNullableAnnotations` and `RespectRequiredConstructorParameters` but **not** `AllowDuplicateProperties`. Its direct counterpart [`ProductCatalogHttpAdapter.cs:58-63`](../../services/Basket/Basket.Infrastructure/ExternalServices/Catalog/ProductCatalogHttpAdapter.cs) sets all three. The two files carry near-identical XML doc-comments explaining the same asymmetry rationale, which makes the divergence look like drift rather than a decision — the Basket adapter's comment (`:46-47`) even documents *why* duplicates are rejected, and the BFF's does not.

**Blast radius.** One file, four typed BFF clients (Basket, Catalog, Inventory, BasketWrite). Behaviour change only for a malformed upstream response. Trivially small.

**Recommendation: adopt.** Cheapest finding in the sweep; it makes two files that should be identical actually identical.

---

## 5. `InvariantGlobalization` — deliberately skip

**Setting.** MSBuild `InvariantGlobalization` / runtimeconfig `System.Globalization.Invariant`. Default `false` ("access to cultural data") per [Globalization config settings](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/globalization).

**Why it was a candidate.** The repo ships Linux-only containers (`RuntimeIdentifiers=linux-x64`, [`Directory.Build.props:21`](../../Directory.Build.props)), and every culture-sensitive call site in the solution already passes `CultureInfo.InvariantCulture` explicitly — verified across Invoicing numbering, PDF generation, blob naming, Basket ACL URL building and Inventory `Quantity`. On that evidence the setting looks free.

**Why skip anyway.** This is a **trade-off default, not a back-compat default** — the docs do not recommend it for new applications; it is an image-size and startup optimisation. It fails the category test this sweep is scoped to. Per the [runtime design doc](https://github.com/dotnet/runtime/blob/main/docs/design/features/globalization-invariant-mode.md), enabling it makes `Compare`/`IndexOf`/`LastIndexOf` **always ordinal** and restricts `ToUpper`/`ToLower` to the ASCII range — a silent, repo-wide semantic change to every string comparison, in exchange for a benefit (image size) that ADR-0009's stated profile (≤ 50 rps, laptop-testable, pedagogical) does not ask for.

**Unverified:** whether `TimeZoneInfo.FindSystemTimeZoneById` with an IANA id — used at `services/Notifications/Notifications.Domain/Preferences/QuietHoursCalculator.cs:41` — still resolves under invariant mode on **Windows** local dev. The runtime design doc states only that *"When running on Linux, ICU is used to get the time zone display name. In invariant mode, the standard time zone names are returned instead"*, and does not address id lookup or Windows. Containers are Linux so production is unaffected either way; the risk is to local dev on Windows. Would need a spike to settle.

**Recommendation: skip**, with the reason recorded so the question is not re-opened from scratch. The correctness ledger is neutral-to-negative and the payoff is orthogonal to this repo's stated purpose.

---

## 6. `ValidateOnBuild` / `ValidateScopes` off in deployed environments — skip, but know the trade

**Setting.** `ServiceProviderOptions.ValidateScopes` and `ValidateOnBuild`.

**Framework default.** Per [Dependency injection § Scope validation](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection#scope-validation), the host enables scope checks only *"When an app runs in the development environment and calls `CreateApplicationBuilder`"*. `ValidateOnBuild` is off by default in all environments.

**Current repo state — verified.** [`WebApplicationBuilderExtensions.cs:78-82`](../../platform/Platform.ServiceDefaults/WebApplicationBuilderExtensions.cs) sets both to `!isClusterEnvironment`.

**Assessment.** The repo is **at or above** the framework default: `ValidateScopes` matches it, and `ValidateOnBuild` is *better* than default in dev, where the framework gives you nothing. So this is not an unadopted recommendation — there is no Microsoft guidance saying to enable either in production.

The trade worth naming: a captive dependency (scoped captured by a singleton) that dev never exercises will not be caught before production. `ValidateOnBuild`'s cost is one-time at boot, and these are long-running services, so the usual argument against it (startup latency) is weak here. But it is a hardening choice, not an unadopted default.

**Recommendation: skip** as out-of-category. Worth a separate conversation on its own merits — flagged here so the reasoning is on record, not to be actioned as part of this sweep.

---

## 7. Npgsql `Max Auto Prepare` — skip, the vendor argues against it

**Setting.** Connection-string parameter `Max Auto Prepare`. Per [Npgsql § Prepared statements](https://www.npgsql.org/doc/prepare.html), it "determines how many statements can be automatically prepared on the connection at any given time (this parameter **defaults to 0, disabling the feature**)". Companion `Auto Prepare Min Usages` "defaults to 5".

**Current repo state — verified.** Not set. `Max Auto Prepare` returns zero hits across all `appsettings*.json`, `.cs` and `docker-compose.yaml`. Connection strings live in `docker-compose.yaml` and carry no preparation settings.

**Why skip — this is the interesting part.** This looked like a textbook candidate (off by default, meaningful throughput win) but the primary source **recommends against relying on it**:

> if you're coding directly against Npgsql or ADO.NET, explicitly preparing your commands with `Prepare()` is still recommended over letting Npgsql prepare automatically

and:

> Automatic preparation is a complex new feature which should be considered somewhat experimental; test carefully, and if you see any strange behavior or problem try turning it off.

A default of `0` that the vendor itself calls experimental is a **trade-off default, not a legacy default**. It fails the category test, and ADR-0009's profile (≤ 50 rps) means the throughput argument does not bite. Correct as-is.

---

## 8–15. Negative findings

Hunted, and found already correct or out of category. Listed because "we checked and it's fine" is worth as much as a finding.

**8. MSBuild / compiler knobs — already at or above the recommended bar.** [`Directory.Build.props`](../../Directory.Build.props) already sets `Nullable=enable`, `AnalysisLevel=latest`, **`AnalysisMode=All`** (the strictest setting; there is nothing above it), `TreatWarningsAsErrors=true`, `CodeAnalysisTreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true`, `CentralPackageTransitivePinningEnabled=true`, `RestorePackagesWithLockFile=true`, and `ContinuousIntegrationBuild` on CI only. This is a stronger baseline than most greenfield .NET repos ship with. No finding.

**9. `NuGetAudit` — correct by default on `net10.0`, no action needed.** Zero hits repo-wide, which is the *right* state: per [Breaking change: 'dotnet restore' audits transitive packages](https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/10.0/nugetaudit-transitive-packages), `NuGetAuditMode` defaults to `all` when projects target .NET 10 or higher. (It was `direct` for ≤ .NET 9 — briefly `all` in a .NET 9 preview, reverted in 9.0.101.) Setting it explicitly would be redundant. The `NoWarn;NU1903` at [`test/Directory.Build.props:71`](../../test/Directory.Build.props) is scoped to the non-shipping test tier and carries a written rationale — deliberate, not drift.

**10. A false alarm worth recording.** `.editorconfig:24` contains `dotnet_analyzer_diagnostic.severity = none`, which read in isolation looks like it nullifies `AnalysisMode=All` solution-wide. It does not: line 22 scopes that block to `[**/Persistence/Database/Migrations/*.cs]`, i.e. EF-generated migrations only. Flagged here because it is the kind of line that gets mis-reported by a grep-only audit.

**11. EF Core 10 flags — verified null result.** The what's-new page was fetched directly and read in full. No opt-in-for-back-compat *flag* applies to this repo:

- `UseNamedDefaultConstraints` — opt-in, but `SqlServerModelBuilderExtensions`; inapplicable to Postgres.
- `UseParameterizedCollectionMode` — EF10 **changed the default** to one scalar parameter per element, with padding. Nothing to adopt; the new default is the recommended behaviour.
- Redacting inlined constants from logs, and the raw-SQL string-concatenation analyzer — both **new defaults in EF10**, already active.
- SQL Server 2025 `json` / `vector` types — inapplicable to Postgres.
- `ConfigureWarnings` appears nowhere in the repo, but it has no recommended-on default; it is a per-app policy choice, not a legacy default.

The one EF10 "advised to switch" item that *does* apply is owned-vs-complex types, promoted out of this list to §3. Provider-specific Npgsql opt-ins were not swept.

**12. Kafka durability knobs — already pinned explicitly.** The outbox relay is the only producer path (outbox pattern), and it pins all three durability settings in [`Platform.OutboxRelay.WorkerService/appsettings.json:38-40`](../../platform/Platform.OutboxRelay.WorkerService/appsettings.json): `Acks: All`, `EnableIdempotence: true`, `MaxInFlight: 5`. On the consumer side `PartitionAssignmentStrategy = CooperativeSticky` is set in every BC (e.g. `Catalog.Infrastructure/Common/MessagingDependencyInjection.cs:73`, and identically in Inventory ×3, Invoicing ×3, Notifications) — an explicit opt-out of librdkafka's non-cooperative default, governed by ADR-0027. No finding.

**13. StackExchange.Redis `abortConnect` — already correct.** Microsoft's guidance is explicit: *"When you use the StackExchange.Redis client library, set `abortConnect` to `false` in your connection string. We recommend letting the `ConnectionMultiplexer` handle reconnection"* ([Best practices for connection resilience](https://learn.microsoft.com/en-us/azure/azure-cache-for-redis/cache-best-practices-connection)). The library default is `true`. Every Redis connection string in [`docker-compose.yaml`](../../docker-compose.yaml) (7 occurrences — lines 510, 571, 619, 669, 764, 765, 812) already carries `abortConnect=false`. No finding.

The same page recommends a five-second connect timeout; the repo uses `connectTimeout=2000`. That guidance targets a managed cloud cache reached across a network — for a same-host `docker compose` Redis under ADR-0009's laptop-testable profile, 2 s is defensible. Noted, not filed.

**14. FusionCache — no recommended-on-by-default flag exists.** Checked `IsFailSafeEnabled` (default `false`), `FactorySoftTimeout` / `FactoryHardTimeout` (default `none`) and `AllowBackgroundDistributedCacheOperations` (default `false`) against [FusionCache Options](https://github.com/ZiggyCreatures/FusionCache/blob/main/docs/Options.md). FusionCache recommends enabling none of them by default — the only guidance offered is that background distributed operations give "a perf boost, but watch out for rare side effects". These are trade-off defaults, and the repo has already picked a side deliberately: `IsFailSafeEnabled = false` at [`Basket.Infrastructure/Persistence/PersistenceDependencyInjection.cs:57`](../../services/Basket/Basket.Infrastructure/Persistence/PersistenceDependencyInjection.cs), with the reasoning in a comment ("baskets must not auto-renew", basket.md § 5.3). No finding.

**15. OpenTelemetry exemplars — already adopted.** `SetExemplarFilter` is configured in every service (e.g. `Basket.Infrastructure/Common/ObservabilityDependencyInjection.cs:88-90`, and identically in the outbox relay and saga), using `TraceBased` in deployed environments and `AlwaysOn` otherwise. `AspNetCoreInstrumentationOptions.RecordException` is deliberately `false` at `ObservabilityDependencyInjection.cs:53` with the rationale inline ("handled in tracing behavior"). No finding.

---

## Coverage and remaining gaps

**Swept against primary sources:** System.Text.Json; ASP.NET Core / FastEndpoints hosting; `Microsoft.Extensions.*` DI + Options validation; EF Core 10; Npgsql statement preparation; StackExchange.Redis connection settings; FusionCache entry options; KafkaFlow / Confluent producer + consumer durability; OpenTelemetry; MSBuild, runtime and analyzer switches.

**Known remaining gaps**, named so they are not mistaken for coverage:

- **Npgsql beyond statement preparation** — pooling (`Maximum Pool Size`, `No Reset On Close`, `Enlist`) and EF-Npgsql provider-specific opt-ins were not swept. Only `Max Auto Prepare` was taken to a primary source (§7).
- **Complex types on Npgsql** — blocks §3; see the `Unverified:` note there. Must be settled before any `OwnsMany` site is touched.
- **KafkaFlow's own library defaults** — the Confluent durability knobs were verified (§12), but KafkaFlow middleware/consumer defaults (buffer sizes, offset-store cadence) were not read against its docs.
- **`Microsoft.Extensions.Http.Resilience`** — `AddResilienceHandler` is used only in the BFF ([`BffResilience.cs:25`](../../src/EShop.BFF/EShop.BFF.Infrastructure/Clients/Common/BffResilience.cs)). The Basket→Catalog ACL client ([`CatalogClientDependencyInjection.cs:48`](../../services/Basket/Basket.Infrastructure/ExternalServices/Catalog/CatalogClientDependencyInjection.cs)) has none. A missing-capability asymmetry, not a legacy default — out of category here, but real and worth its own ticket.
