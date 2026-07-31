# ADR-0038: HTTP contract-break detection — a committed artifact, not a registry

## Status

Accepted (2026-07-30)

## Context

A BC's HTTP response shape is a published contract with named consumers, and nothing detects a change
to it. The BFF binds upstream responses by JSON property name only — it references no BC assembly — so
a removed or renamed member reaches production as a runtime degradation ([bff.md § 4](../bc-design/bff.md),
*Accepted gap*). Catalog's `GET /products/by-ids` has two consumers (the BFF and Basket's ACL), so the
blast radius is already cross-BC.

**Kafka has the equivalent guard and HTTP does not** — but not for the reason the shape suggests. What
protects the async side in this repo is neither the Schema Registry nor its compatibility modes:
[`schema-compat-checks.md`](../deployment/schema-compat-checks.md) records that the compat gate is a
documented reference pattern, deliberately **not implemented**, and that an earlier CI implementation was
removed for producing false verdicts against a `main`-derived baseline. The working guard is that
producer and consumer compile against **one generated type** — Catalog and the BFF both reference
`Platform.SchemaRegistry.Contracts`, so an `.avsc` change breaks both builds.

The gap is therefore **a shared artifact, not a broker**. Two findings settle that the broker half is
not merely unbuilt but not transferable:

- **Avro publishes a reader/writer schema-resolution algorithm; JSON-over-HTTP publishes none.** Confluent's
  BACKWARD/FORWARD modes are checkable because "compatible" has a formal referent. For OpenAPI it does not,
  which is why every tool invents its own taxonomy — oasdiff carries 212 hand-curated breaking-change rules
  against Avro's compact promotion table.
- **The dividing line is the schema language, not sync-vs-async.** Apicurio rates compatibility enforcement
  *Full* for Avro, Protobuf and JSON Schema and **None** for AsyncAPI; its maintainer names why OpenAPI
  resisted: one `components` schema serves as both request input and response output, so "Compatibility
  rules will be different for inputs vs. outputs." Protobuf-over-HTTP lands on the enforced side; AsyncAPI
  lands on the unenforced side. Async-ness is not the variable.

Evidence base: [`docs/research/http-contract-break-detection.md`](../research/http-contract-break-detection.md).

## Decision Drivers (ranked)

1. **Detect at change time, in the consumer that binds** — a warning that some shape moved is worth far
   less than a failure in the code that depends on it.
2. **Valid for independently-deployed services in separate repos** — the monorepo is an artifact of this
   being a reference solution. A mechanism that only works because both sides sit in one commit teaches
   the wrong thing.
3. **Proportionate, and recognizably industry-standard** — a reference solution earns its keep by
   demonstrating a canonical approach, and loses it by demonstrating machinery nobody runs.
4. **Survives the planned framework moves** — FastEndpoints is expected to give way to minimal APIs, so
   detection must not be built on FastEndpoints-specific or NSwag-specific APIs.

## Considered Options

### Option 1 — Committed OpenAPI artifact + generated client + staleness gate (chosen)

Each producer commits its OpenAPI document under test; the consumer vendors it and generates a typed
client from it at build; a spec diff classifies changes between versions.

### Option 2 — Spec-diff only

Commit the document and diff it in CI; no generated clients.

- Cheapest, and touches no consumer code. But detection stops at the spec: nothing links a diff to the
  fact that *this* consumer bound *that* member, so it warns on every change and can never name who breaks.

### Option 3 — Consumer-driven contract testing (Pact)

- Rejected on a hard incompatibility, not on taste: PactNet cannot host a provider under
  `Microsoft.AspNetCore.Mvc.Testing`, because its Rust FFI core requires a real TCP socket. That is this
  repo's entire integration-test seam. Supporting evidence — 16 months without a release, `netstandard2.0`
  only, native core six releases behind, no plugin support — and the technique's own radar history: adopted
  as a *technique* three times, while Pact the *tool* peaked at Trial in 2015 and has been absent for eight
  volumes. Driver 3 counts against it twice: Microsoft's own eShop reference apps use it nowhere.

### Option 4 — An HTTP schema registry mirroring Confluent's

- Rejected as non-existent rather than unattractive. Apicurio attempted it; the input/output asymmetry
  above is the stated blocker. There is no such thing to adopt.

### Option 5 — Do nothing more

- Rejected on driver 2. The restraint case rests on the monorepo collapsing detection to "the build
  fails", which holds only at *change* time and only for consumers that already exist. It does not cover
  deploy-window skew, and no source with standing publishes a monorepo exemption. Retained in part: it is
  why replication beyond the first seam is discretionary rather than required.

## Evaluation Matrix

| Driver (ranked) | Opt 1: artifact + codegen | Opt 2: spec-diff only | Opt 3: Pact | Opt 4: registry | Opt 5: nothing |
|---|---|---|---|---|---|
| 1. Detect in the binding consumer | Compile error | Warns, names no consumer | Yes, per known consumer | — | Runtime only |
| 2. Polyrepo-valid | Producer half needs no consumer | Yes | Yes | — | No |
| 3. Proportionate + canonical | Ubiquitous | Ubiquitous | Heavy; tool unused in .NET reference apps | — | Free |
| 4. Survives framework moves | Document fetched over HTTP | Same | Independent | — | n/a |

## Decision

**Three layers, each with one job. Mirror the artifact half of the Avro model; do not build the registry half.**

1. **Producer staleness** — each unit's integration tests fetch `/swagger/v1/swagger.json` from the
   existing `WebApplicationFactory` host and snapshot it against a committed document. Deliberately an
   **HTTP GET rather than a generator API**, so the mechanism survives both FastEndpoints → minimal APIs
   and NSwag → `Microsoft.AspNetCore.OpenApi` (driver 4).
2. **Break classification** — oasdiff compares the committed document at merge-base against HEAD,
   failing on `ERR`.
3. **Consumer detection** — the consumer references the producer's committed document **directly, at a
   deterministic path** (`services/<BC>/<BC>.Api/openapi/<bc>-v1.json`), and generates a typed client from
   it at build. The generated client performs the call and the deserialization; its types are mapped into
   the hand-written ACL records. A removed or renamed member the consumer binds becomes a **compile error**
   — in the same commit that removes it.

   **The path is the only part that is repo-layout-specific.** The mechanism — committed document →
   `<OpenApiReference>` → generated client → compile error — is identical across layouts; in separate
   repos the same file arrives as a release artifact or a package and the reference points there instead.
   A consumer-local copy was considered and rejected: it buys nothing a separate repo would have (there,
   a version bump forces the refresh; here nothing would), while costing a duplicate per seam with no
   check that the copies agree.

**The document must carry `required`.** `MarkNonNullablePropsAsRequired()` is mandatory on every document.
Without it NJsonSchema emits no `required` at all under `SchemaType.OpenApi3` — the C# 11 `required`
modifier is invisible to it — which silently defeats layers 2 and 3 together: oasdiff downgrades every
member removal to a warning, and the generator emits all-optional types. Note the resulting contract is
**nullability-driven, not `required`-driven**, and that is the intended semantic: the document states what
a consumer may rely on being present and non-null, which is precisely what `RespectNullableAnnotations`
enforces on the binding side. `required` governs construction; it is not a wire property.

**The generated client complements the ACL; it does not replace it.** A generated client reproduces the
provider's model in the consumer's process — that is the Conformist pattern, and a generator has no
knowledge of this repo's domain to translate into. The per-route ACL records and the ownership rule they
carry ([bff.md § 4](../bc-design/bff.md)) stand unchanged; generation feeds them.

**The artifact pins structure, not deployment metadata.** `servers` and the OAuth2 scheme URLs are
environment-derived — NSwag's middleware overwrites `servers` from the request URL unconditionally, and
the security scheme is built from `Authentication:JwtBearer:Authority`, which a deployed tier supplies and
the base configuration deliberately omits. Both are normalized out of the snapshot. Neither layer would
have caught the drift: oasdiff defines no rules for server changes and rates security-scheme changes below
its breaking-change floor.

**Publication is out of scope, and the committed file is the artifact.** Deployed tiers serve no document
(`UsePlatformAuthSwaggerGen` is gated on `!IsDeployedEnvironment()`), so there is no live endpoint to
scrape and never was. A consumer in another repo takes the same committed file from released source or a
release asset. The layout changes where it is read from, not what it is.

**Backing out is cheap, and that is deliberate.** If generation proves more trouble than the signal is
worth, delete the `<OpenApiReference>` items and restore each client's hand-rolled `ReadFromJsonAsync`
into the same ACL records — those records are untouched by this decision and remain the binding contract
either way. Layers 1 and 2 stand alone and would survive that reversal.

## Rationale

Driver 1 is what separates option 1 from option 2, and it is the whole point: a spec diff can say a shape
moved, but only the consumer's compiler knows whether *this* consumer read the member that vanished. That
is the same guarantee the Avro path already gives the async side, obtained the same way — one artifact,
two builds.

Driver 2 is why the producer half ships first and stands alone. Layers 1 and 2 are complete and valuable
with **zero consumers**: they protect parties nobody can enumerate, which is the actual polyrepo condition,
and they are what tells a producer it is breaking someone it cannot see. Driver 2 does *not* argue for a
consumer-local copy of the document — that would imitate a polyrepo's file layout while reproducing none
of its forcing function, and the mechanism it would protect is identical either way.

Restraint survives where the evidence supports it. Replication past the first seam is **discretionary** —
the fourth repetition demonstrates nothing the first did not, and driver 3 counts against volume.

## Consequences

### Positive

- A removed or renamed member a consumer binds fails that consumer's build, matching the guarantee the
  Avro contracts already provide.
- Layers 1 and 2 protect unenumerable consumers, which is the case the compiler cannot reach.
- Every producer gains a reviewable contract diff; a wire change becomes visible in a PR rather than
  inferred from a handler.

### Negative

- Each generated route carries a mapper from generated type to ACL record. Accepted: it is what keeps the
  ACL records declaring only what a page renders.
- A producer PR that trims a contract cannot merge until every in-repo consumer is fixed in the same PR.
  Accepted, and the reason to prefer it: that is the atomicity a monorepo exists for, and it makes the
  break visible at the moment it is introduced rather than whenever someone next refreshes.

### Risks

- **A compile error proves consistency with the committed document, not with the running provider.** It
  does not catch an added enum value (which throws at deserialization), a nullability change, a status-code
  change, or a semantic change at an identical shape. The producer-side payload pinning tests remain the
  guard for those.
- **`ShortSchemaNames = true` makes colliding schema names position-dependent.** FastEndpoints discards the
  namespace and delegates collisions to NJsonSchema, which allocates `Foo` / `Foo2` in document-traversal
  order — so an unrelated endpoint change can swap which server shape a generated type binds, and still
  compile. No collision exists today (no BC declares two same-named types), and the setting is kept for the
  readable names it gives generated code. **Mitigation: each producer's snapshot test asserts no
  `components.schemas` key is a numeric-suffix sibling of another.** This fires the revisit trigger
  [ADR-0037 § Risks](0037-endpoint-owned-response-contracts.md) recorded for the return of generated
  clients; the assertion is chosen over the custom `SchemaNameGenerator` that ADR weighed, because it
  checks the emitted artifact rather than approximating it from type names, and it needs no platform code.
  Note the collision surface is wider than declared type names alone — generic arguments contribute
  `Type.Name` in both modes, and framework types on the wire contribute too.
- **A query parameter has one encoding in the document.** Catalog's `by-ids` route was reached with two
  (comma-joined and repeated), which no document can describe; it is normalized to repeated parameters,
  the OpenAPI 3.0 default and the only form minimal-API binding accepts.

## Implementation Notes

- The generated client's serializer must carry **whatever `UpstreamJson.Web` carries**, or generation
  silently discards it: NSwag defaults `JsonLibrary` to `NewtonsoftJson`, which has no equivalent of any of
  those settings. Override to `SystemTextJson` and implement the generated
  `static partial void UpdateJsonSerializerSettings(JsonSerializerOptions)` hook, feeding it the same
  options object rather than a second copy of the list — a divergence between the two is silent.
- **A generated client throws on every non-2xx, which the result-pattern rule forbids escaping the ACL.**
  An upstream 404 is an *expected* error modelled in the BFF's failure tables, not an exceptional one. The
  ACL wrapper catches the generated `*ApiException`, switches on `StatusCode`, and returns a `Result` —
  exceptions stay inside the anti-corruption layer, which is what that layer is for.
- **The NSwag option set is one decision, not one per client.** With four BFF clients plus Basket's, the
  switch list is hoisted to a shared MSBuild property and each `<OpenApiReference>` appends only what is
  genuinely per-client (`ClassName`, `Namespace`, `ExceptionClass`). Restating a twelve-switch string per
  reference is the drift surface [ADR-0037](0037-endpoint-owned-response-contracts.md) warns about.
- **Let generated code land in `obj/`.** Coverage exclusion (`coverlet.runsettings`) and `dotnet format`'s
  generated-file skip both key off that location; directing output into the project tree re-opens both.
- oasdiff needs a full-depth checkout and a skip-if-absent guard: it exits non-zero when the document does
  not exist at merge-base, which is true on the very commit that introduces each one.
- The snapshot test asserts the contract *document*; the per-endpoint raw-JSON payload tests assert the
  emitted *payload*. Neither replaces the other — a mapper bug changes the payload without changing the
  document.

## Related Decisions

- [ADR-0037: Endpoint-Owned Response Contracts](0037-endpoint-owned-response-contracts.md) — governs the
  shapes this ADR pins. Its recorded revisit trigger ("if generated clients return, a position-dependent
  name becomes a consumer-breaking change") is fired here and answered in Risks.
- [ADR-0007: Avro Schema Compatibility Modes](0007-avro-compatibility-modes.md) — the async policy whose
  *artifact* half this ADR mirrors and whose *registry* half it deliberately does not.
- [ADR-0033: SSOT for Kafka topic & event-contract documentation](0033-kafka-topic-contract-doc-ssot.md) —
  the same preference for a derived, checkable artifact over hand-maintained restatement.
- [ADR-0012: API Versioning](0012-api-versioning.md) — orthogonal. This ADR detects changes within a
  version; a version bump creates a new contract by construction.
