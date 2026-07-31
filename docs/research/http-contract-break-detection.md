# Detecting that a synchronous HTTP contract change broke a consumer

Research note (2026-07-30). Every number, ring, version and status below was fetched on that date unless stated. No recommendation is offered — the evidence is presented so that any conclusion drawn from it can be argued against using the same sources.

Where a claim could not be reached in a primary source it is marked **Unverified:** with what was tried. Where a source contradicts a widely-repeated belief, both are stated.

## Reading guide

- **§1** frames what "a break" even is — the taxonomies disagree, and that disagreement is upstream of every tool choice.
- **§2–§16** are one family per section: what it catches, what it structurally cannot catch, the machinery, the named failure modes, and the critique at full strength.
- **§17–§19** are the evidence sections: usage numbers, ecosystem-survival data, radar movement, and where credible people disagree and why.
- **§20** is the .NET reality table. **§21** answers the generated-client-vs-anti-corruption-layer question. **§22–§23** are the two sub-questions (why async got registries; the case for restraint).
- **§24** lists negative findings — things looked for and not found. Several are decision-relevant.
- **§25** is the summary matrix.

Load-bearing sections, if reading selectively: §7 (spec-diff), §8 (generated clients), §15 (versioning discipline), §17–§18 (what is actually used, and radar movement), §20 (.NET), §21 (ACL), §22 (the async/sync asymmetry), §24 (negative findings).

Three findings most likely to change a conclusion, flagged here so they are not missed: **§18** — consumer-driven contract testing reached ThoughtWorks' *Adopt* ring as a **technique** and was retired, while Pact the **tool** never left *Trial*; **§20** — there is no first-party way in .NET to fail CI on a stale OpenAPI document, verified at code level; **§20** — `ShortSchemaNames` is a FastEndpoints option, not NSwag's, and it makes generated schema names depend on document traversal order.

---

## 1. There is no agreed definition of "breaking"

The most-cited authorities classify the *same* change differently. This is not pedantry: every tool in §7 ships a vendor-invented ruleset because no standard exists to implement.

| Change | Google AIP-180 | GitHub REST | Azure | Zalando |
|---|---|---|---|---|
| Add a response field | non-breaking (implied) | **not breaking** | non-breaking | non-breaking (#107) |
| Add value to a **response** enum | **MAY, "with caution"** | **not breaking** | breaking unless the enum is *extensible* | **cannot** extend output enums (#107) |
| Add value to a **request** enum | **MAY**, freely | not breaking | — | permitted (#107) |
| Remove enum value | MUST NOT | breaking | breaking (incl. extensible values) | reduce-for-input allowed if old values still accepted |
| Tighten validation on an existing parameter | *not addressed* | **breaking** | — | MUST NOT (#107) |
| New top-level error code | *not addressed* | breaking (auth changes) | **breaking** | — |
| Change field type, even wire-compatible | MUST NOT | breaking | breaking | MUST NOT |

Sources: [AIP-180 Backwards compatibility](https://google.aip.dev/180), [GitHub REST breaking changes](https://docs.github.com/en/rest/about-the-rest-api/breaking-changes), [Azure REST API Guidelines](https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md), [Zalando #107](https://opensource.zalando.com/restful-api-guidelines/#107).

- **Adding a response enum value — the single most common real change — has no consensus.** GitHub declares it safe; Zalando forbids it outright; Azure permits it only behind an extensible-enum declaration.
- **The OpenAPI Initiative declines to define it.** Maintainer `handrews` closed [OAI discussion #3793](https://github.com/OAI/OpenAPI-Specification/discussions/3793) as out of scope, on the grounds that defining "breaking change" for all APIs would be a different kind of standard. The discussion catalogues three incompatible models of break — on-the-wire, SDK-generation, and functional.
- **Microsoft's older guideline explicitly delegates the definition**: services MUST define their own notion of a breaking change, "especially regarding adding new fields to JSON responses" ([Microsoft REST API Guidelines](https://raw.githubusercontent.com/microsoft/api-guidelines/master/Guidelines.md)).
- **Zalando separates *incompatible* from *breaking*** — an incompatible change is only breaking once deployed against a consumer that actually uses the affected aspect ([#106](https://opensource.zalando.com/restful-api-guidelines/#106)). That distinction reappears as the central axis of §7 (false positives) and §9 (usage-aware checks).
- **Zalando also scopes its guarantee to the wire format only**: source/binary compatibility of generated client code is explicitly *not* covered ([#106](https://opensource.zalando.com/restful-api-guidelines/#106)). Anyone whose detection strategy is "the generated client stops compiling" is detecting something Zalando does not promise.

The break classes any strategy must be judged against:

1. **Structural** — field/endpoint/parameter added, removed, retyped, re-required.
2. **Enumerative** — a new member in a closed set.
3. **Nullability** — a value that could not be null now can.
4. **Protocol** — status codes, headers, auth, content negotiation, URL shape.
5. **Validation** — same shape, narrower accepted input.
6. **Semantic** — same shape, different meaning (units, timezone, sort order, pagination default, consistency guarantee).
7. **Behavioural side effects** — the call still returns 200 but no longer persists what it used to.

No single family in this document covers all seven. Most cover one or two.

---

## 2. Consumer-driven contract testing (Pact)

**The pattern, from its authors.** [Ian Robinson, 2006](https://martinfowler.com/articles/consumerDrivenContracts.html) defines three contract types — provider contracts (singular, authoritative), consumer contracts (one per consumer), and consumer-driven contracts (singular, derived from the union of active consumer contracts, non-authoritative). Two caveats in the original article are routinely dropped when the pattern is cited:

- It is scoped to "a single enterprise or a closed community of well-known services" — Robinson, same URL.
- It does **not** reduce coupling. Robinson says CDC "excavate[s]" couplings already present and puts them on display, and warns that letting consumer demands drive the provider risks the provider's conceptual integrity.

[Ham Vocke's Practical Test Pyramid](https://martinfowler.com/articles/practical-test-pyramid.html) states the mechanism — "The consumer drives the implementation of a contract" — and names the scoping condition: inside your org you can do this because your app "will most likely serve a handful of consumers max", whereas public APIs "can't consider every single consumer".

**A CI-philosophy conflict at the root.** [Fowler's ContractTest](https://martinfowler.com/bliki/ContractTest.html) says a contract-test failure "shouldn't necessarily break the build in the same way" — it should trigger a reconciliation conversation. Pact ships the opposite: `can-i-deploy` is a hard gate.

### What it catches
- Structural and enumerative breaks **on exactly the interactions a consumer recorded**, per consumer, with a computed per-pair verdict.
- The one thing nothing else in this document does: it enumerates the **actual consumer set** and gives a deploy-order answer. [`can-i-deploy`](https://docs.pact.io/pact_broker/can_i_deploy) inspects the verification matrix for compatibility with the versions already in the target environment.

### What it cannot catch
- **Side effects.** Pact's own docs: "A contract test does not check for side effects" ([contract tests vs functional tests](https://docs.pact.io/consumer/contract_tests_not_functional_tests)). Whether the provider does the right thing with the request is the provider's functional tests' job.
- **Anything a consumer did not record.** PactFlow concedes the checks "may say it is safe to deploy when it could not be" if the consumer contract omits interactions the consumer actually uses (**Unverified:** the PactFlow compatibility-checks doc 404'd on two path variants and the SmartBear mirror returned 403; this quote is snippet-sourced and should be re-fetched before being relied on).
- **Semantic change with unchanged shape** — outside the model entirely.
- Loosening validation *should not* fail a pact; over-specification is named as the failure mode (same docs page).

### Machinery
- A **Pact Broker** (self-hosted or PactFlow) is required for the parts that pay — `can-i-deploy` reads its matrix ([docs](https://docs.pact.io/pact_broker)).
- **Provider states**: preconditions injected into the provider's datastore before each interaction ([docs](https://docs.pact.io/getting_started/provider_states)).
- **CI surgery on both pipelines.** Pact's own adoption ladder has **seven graded steps** — Get Prepared, Talk, Bronze, Silver, Gold, Platinum, Diamond — and the value sits at steps 5–7: PR-pipeline integration, `can-i-deploy` with branch tags, then deploy-pipeline integration ([Pact Nirvana](https://docs.pact.io/pact_nirvana)).

### Where Pact itself says not to use it
Verbatim items from [What is Pact good for?](https://docs.pact.io/getting_started/what_is_pact_good_for) — Pact is **not** good for:

- APIs where the other side "will not also be using Pact"
- "Testing APIs where the consumers cannot be individually identified (eg. public APIs)"
- Cases where you cannot load provider data without using the API under test
- Providers whose functionality is not driven by consumer needs
- Teams with poor communication channels
- Performance and load testing; provider functional testing
- **"Pass through" APIs that forward requests downstream without validating them**
- General-purpose mocking for browser-driven tests

The pass-through exclusion is directly relevant to any BFF that forwards mutations to a downstream service without adding validation.

### The critique — and its strongest form comes from inside Pact

- **Beth Skurrie (Pact's founder)** documents a company that wrote UI-driven pacts and **removed them**: "Their Pact tests were painful"; provider verification "failed often just because of the data set up, not because of any API incompatibilities" ([A disastrous tale of UI testing with Pact](https://pactflow.io/blog/a-disastrous-tale-of-ui-testing-with-pact/)).
- **Skurrie's four named failure causes** ([Why Pact implementations fail](https://pactflow.io/blog/why-pact-implementations-fail-and-what-you-can-do-to-avoid-it-blog/)): lack of buy-in; lack of education; misaligned cost/benefit under the subheading "The people paying the cost don't get to experience the benefits"; and an org that does not value backwards compatibility, where "[f]ailing contract tests are seen as an impediment".
- **Matt Fellows (maintainer, PactFlow co-founder)** enumerates CDC's costs himself ([Pact is dead, long live Pact](https://pactflow.io/blog/bi-directional-contracts/)): "It can take time to get value from Pact"; "The consumer-driven approach does not scale to large numbers of consumers"; "It can be hard to get provider teams on board with Pact"; it "requires access to the underlying code base"; "Implementing provider states can be challenging"; providers "feel as though they are 'doubling up' test effort".
- **Fellows, 2026, on adoption**: "adoption by some teams was slow, not because of the idea, but because of the cost" ([Schemas can be contracts](https://pactflow.io/blog/schemas-can-be-contracts/)).
- **Independent analysis**: Craig Risi lists seven cons including a learning curve spanning consumer-driven contracts, verification and the Broker; broker infrastructure to "maintain and administer"; and contracts that "can become outdated or too rigid over time" ([The Pros and Cons of Using Pact](https://www.craigrisi.com/post/the-pros-and-cons-of-using-pact-for-contract-testing)).
- **Uneven cross-language parity**, from Microsoft's own ISE team: "[e]nsure you verify the level of support before committing to using PACT in a less-supported language" ([devblogs.microsoft.com/ise](https://devblogs.microsoft.com/ise/pact-contract-testing-because-not-everything-needs-full-integration-tests/)).

### The counter-argument at full strength
- **The case for Pact is mostly a case against E2E.** Fellows: E2E tests "are *slow*", "are hard to *maintain*", "can be *unreliable or flakey*", "*scale* badly", and "*find bugs too late*" ([What is contract testing](https://pactflow.io/blog/what-is-contract-testing/)). Contract tests run without talking to multiple systems, surface failures on developer machines, and enable developing the consumer before the API exists.
- **Against "just use OpenAPI"**: Fellows' [Schemas are not contracts](https://pactflow.io/blog/schemas-are-not-contracts/) — "Schemas are abstract, contracts are concrete." A schema cannot express relationships between data elements, cannot say which fields a consumer can reliably expect, cannot pin which inputs produce which status codes, and "you can't track the specific needs of each consumer: you must assume they use the entire set of features."
- **"You're using it wrong" is argued by Pact's own founder, not by defenders against critics.** The documented anti-patterns are consumer-side: exact matching across app layers, UI-driven pacts, and asserting constraints the consumer does not depend on — "especially your UI, you will drive yourself nuts" ([docs.pact.io/consumer](https://docs.pact.io/consumer)).

---

## 3. Bi-directional contract testing (PactFlow) — the vendor's own escape hatch

PactFlow's comparison of CDC vs bi-directional contract testing is, in effect, the vendor conceding CDC is too heavy for many teams ([Difference between CDC and BDCT](https://pactflow.io/difference-between-consumer-driven-contract-testing-and-bi-directional-contract-testing/)):

| Aspect | Consumer-driven | Bi-directional |
|---|---|---|
| Learning curve | **"Consumer driven contract testing has a steep learning curve."** | Conceptually simpler |
| Framework | New Pact framework needed | Works with existing OpenAPI, WireMock, Postman, Dredd |
| Code access required | Yes | No |
| Participants | Developers primarily | Testers, QA, broader teams |
| API gateway / 3rd-party provider | Not ideal | Better suited |
| API-first / provider-first publishing | Not suited | Supported |

- **Model**: the consumer publishes what it uses; the provider publishes its spec; the broker cross-checks the two and gates the deploy. No provider states, fully decoupled pipelines ([Fellows](https://pactflow.io/blog/bi-directional-contracts/)).
- **Cost**: it is a **paid PactFlow/SmartBear feature**, not in open-source Pact ([product page](https://pactflow.io/bi-directional-contract-testing/)).
- **What it inherits**: because the provider side is a *spec*, BDCT inherits every weakness in §7 — nothing proves the provider implements the spec beyond its own functional suite. PactFlow later shipped "Drift" specifically to close that hole ([Schemas can be contracts](https://pactflow.io/blog/schemas-can-be-contracts/)).
- **Unverified:** PactFlow publishes no explicit "what BDCT cannot do" list. The inheritance argument above is inference from their own [Schemas are not contracts](https://pactflow.io/blog/schemas-are-not-contracts/).

---

## 4. Provider-driven contract publishing / spec-first

The provider authors the contract, publishes it, and generates its own verification from it. Consumers consume the artifact.

- **Spring Cloud Contract** was the JVM flagship, supporting both consumer-driven and provider-driven modes, generating provider tests plus WireMock consumer stubs ([reference docs](https://docs.spring.io/spring-cloud-contract/reference/index.html)).
- **It was moved to the Spring attic on 2026-07-06.** The README's first line is now: "Spring Cloud Contract is no longer actively maintained." The repo is `archived: true`, homepage redirected to `spring-attic` (`gh api repos/spring-cloud/spring-cloud-contract` → `archived=true`, `latest=v5.0.3 2026-06-11`, HEAD commit 2026-07-06 "Update README with maintenance notice").
- Maintenance passed to **Stubborn.sh**, run by original project lead Marcin Grzejszczak. Jason Konicki's announcement gives stewardship, not adoption or burden, as the reason: "returning the project to its roots under the Stubborn.sh umbrella is the best path forward" ([spring.io blog, 2026-07-06](https://spring.io/blog/2026/07/06/spring-cloud-contract-transition-to-stubbornsh)).
- **The successor has essentially no community yet**: `stubborn-sh/stubborn-contract` ("the continuation of Spring Cloud Contract") has **5 stars**; the whole org's repos are at 0–5 stars (`gh api orgs/stubborn-sh/repos`).
- Pact's own comparison concedes Spring Cloud Contract is "easier for you to integrate into your tests" on JVM/Spring and better "if your workflow is inherently more provider driven" ([comparisons](https://docs.pact.io/getting_started/comparisons)).
- **.NET relevance: none.** Spring Cloud Contract is Spring/Java-only; the docs mention no .NET support.

**What this family catches**: whatever the provider chose to specify, verified against the provider's implementation. **What it misses**: which consumers exist, and what they actually use — the provider defines the contract unilaterally, which is precisely what CDC was invented to fix.

---

## 5. Schema registries (Confluent) — and whether an HTTP analogue exists

### The model, and why it is checkable

[Confluent's compatibility types](https://docs.confluent.io/platform/current/schema-registry/fundamentals/schema-evolution.html):

| Type | Allowed changes | Checked against | **Upgrade first** |
|---|---|---|---|
| BACKWARD (default) | add optional fields, delete fields | last version | Consumers |
| BACKWARD_TRANSITIVE | same | all previous | Consumers |
| FORWARD | add fields, delete optional fields | last version | Producers |
| FORWARD_TRANSITIVE | same | all previous | Producers |
| FULL | add/delete optional fields only | last version | Either |
| FULL_TRANSITIVE | same | all previous | Either |
| NONE | all | checking disabled | No guarantee |

- **The upgrade-order column is the artifact nothing on the HTTP side produces.** The registry does not merely answer yes/no — it tells you which side of the deployment must move first.
- **The default is BACKWARD and non-transitive**, chosen because it "allows you to rewind consumers to the beginning of the topic" (same URL). Replay is the stated reason for the default — see §22.
- **Pre-deploy checks exist and are separate from runtime**: the dry-run `POST /compatibility/subjects/{subject}/versions/{version}` endpoint with `?verbose=true` ([API docs](https://docs.confluent.io/platform/current/schema-registry/develop/api.html)), and the Maven goals `schema-registry:validate` (local) and `schema-registry:test-compatibility` (against a live registry) ([plugin docs](https://docs.confluent.io/platform/current/schema-registry/develop/maven-plugin.html)). **Unverified:** the docs never state that `test-compatibility` *fails the Maven build*; that is inference from goal semantics.
- **What the registry does not check.** There is no single disclaimer sentence; the admission is structural. Confluent's data-contracts page defines a contract as covering "structure **and semantics**", and lists the schema as supplying only **Structure** — integrity constraints, metadata, rules/policies and evolution are separate elements ([data contracts](https://docs.confluent.io/platform/current/schema-registry/fundamentals/data-contracts.html)). Confluent shipped Data Contracts precisely because compatibility modes are structural.

### Is there an HTTP analogue? Almost, and the near-miss is instructive

- **Apicurio Registry** accepts OpenAPI as an artifact type and publishes a rule-maturity matrix rating **OpenAPI compatibility = Full** ([rule reference](https://www.apicur.io/registry/docs/apicurio-registry/3.1.x/getting-started/assembly-rule-reference.html)).
- **That claim does not survive contact with the maintainer.** In [discussion #1696](https://github.com/Apicurio/apicurio-registry/discussions/1696) (2023-04-26), Eric Wittmann says it is "still on the bucket list" and "we honestly haven't made a ton of concrete progress", and names the blocker: JSON Schema definitions in `components` "can be used either as inputs or as outputs (or both) for various operations. Compatibility rules will be different for inputs vs. outputs".
- **That is the sharpest technical explanation for the missing HTTP registry in this entire document**, and it comes from someone who tried to build one. One OpenAPI document needs *opposite* compatibility directions for the same schema depending on whether it is a request body or a response body. A Kafka message has exactly one direction.
- **Doc-vs-reality conflict, stated rather than resolved**: the published matrix says Full; the maintainer says unimplemented. Apicurio: 875 stars, Apache-2.0, 485 open issues, pushed 2026-07-30.
- **Two things in this survey do reject a publish on breaking-change grounds** — Buf's BSR (§10) and Apollo GraphOS / GraphQL Hive (§9). Neither speaks OpenAPI.
- **Azure API Center** lints on definition upload and "generates a report", with breaking-change detection only as an author-time VS Code feature ([enable API analysis](https://learn.microsoft.com/en-us/azure/api-center/enable-api-analysis-linting), [VS Code extension](https://learn.microsoft.com/en-us/azure/api-center/govern-apis-vscode-extension)). Inventory plus advisory, not a gate.
- **AWS API Gateway** model validation is per-request at runtime, returning 400 — categorically payload validation, not schema evolution ([request validation](https://docs.aws.amazon.com/apigateway/latest/developerguide/api-gateway-method-request-validation.html)).
- **Backstage** validates that a catalog entity has no schema errors ([catalog API](https://backstage.io/docs/features/software-catalog/software-catalog-api/)). No compatibility rule, no publish gate.
- **The Pact Broker is the closest true analogue to Confluent's upgrade-order guarantee** — and note *how*: it does not reason about schemas at all. It enumerates known consumers and requires green pairwise verifications, substituting an explicit consumer registry for the schema-resolution model Avro gets from its spec.

---

## 6. Runtime request/response validation at the edge

A distinct family: enforce the spec on live traffic rather than diff two specs.

- **Prism** (Stoplight) runs as a mock server *or* a validation proxy that funnels HTTP traffic through it to verify the implementation matches its OpenAPI document. 4,994 stars, Apache-2.0, `v5.16.0` 2026-07-17, pushed 2026-07-25 ([repo](https://github.com/stoplightio/prism)).
- **Redocly CLI `drift`** — "Detect drift between recorded HTTP traffic and an OpenAPI description", marked **experimental** ([commands](https://redocly.com/docs/cli/commands)). Note the axis: spec-vs-implementation, not version-vs-version.
- **Kusk Gateway** made OpenAPI the source of truth for routing plus request validation at the gateway — 281 stars, **last push 2023-02-21** ([repo](https://github.com/kubeshop/kusk-gateway)). Effectively dead.
- Node/Ruby ecosystem equivalents: [openapi-backend](https://github.com/anttiviljami/openapi-backend) (683 stars), [committee](https://github.com/interagent/committee) (952 stars).
- **Criteo built one and published the numbers.** Sampling 1% of production gateway traffic against their published spec, Jean Baptiste Muscat found "some endpoints that are invalid on ~5% of the calls", and concluded that if the spec is inaccurate then "your breaking change detection is worthless" ([Can you trust your OpenAPI spec?](https://medium.com/criteo-engineering/can-you-trust-your-openapi-spec-a62677d43fb3), 2024-11-21).
- **The irony is load-bearing**: Criteo also *wrote* the .NET OpenAPI spec comparator (§20) — and separately measured that the specs such a comparator diffs are wrong 5% of the time.

**Catches**: drift between the spec and the running implementation, which is the precondition for every spec-based approach. **Misses**: everything about the *consumer* — it never tells you whether anyone depended on what drifted.

---

## 7. OpenAPI spec-diff + linting in CI

### oasdiff — the only tool here with a real breaking-change ruleset

- Canonical org is **`oasdiff/oasdiff`**; `tufin/oasdiff` is a rename-redirect, though the Docker image is still published under the old namespace ([repo](https://github.com/oasdiff/oasdiff)).
- 1,299 stars, 35 open issues, Apache-2.0, pushed 2026-07-30, latest `v1.26.1` 2026-07-27. **Docker pulls: 4,218,203** ([Docker Hub API](https://hub.docker.com/v2/repositories/tufin/oasdiff/)).
- **506 total change rules — 212 breaking, 30 warning, 264 informational** across 9 OpenAPI areas ([breaking changes docs](https://www.oasdiff.com/docs/breaking-changes)). **Discrepancy flagged:** the in-repo doc says only "hundreds of checks"; the 506/212 figures appear only on the vendor docs site ([BREAKING-CHANGES.md](https://raw.githubusercontent.com/oasdiff/oasdiff/main/docs/BREAKING-CHANGES.md)).
- Severity semantics, verbatim: **ERR** = "definite breaking changes which should be avoided"; **WARN** = "potential breaking changes … cannot be confirmed programmatically as breaking"; **INFO** = "Non-breaking changes" (same URL).
- **Exit codes**: `--fail-on ERR` / `--fail-on WARN` / `--fail-on INFO` return 1. Commands: `breaking`, `changelog`, `diff`, `checks`.
- Per-check severity is overridable via a severity-levels file; `--deprecation-days` allows graceful removal after a signalled window.
- **Stated blind spots, verbatim**: no checks for `context` instead of `schema` on request parameters; no checks for callbacks; documentation-only edits excluded.
- **The tool states its own epistemic limit better than any critic**: it judges "against the API contract your OpenAPI definition declares, not against what a particular server happens to accept", and "a server may quietly accept a request the contract says is invalid" (same URL).
- **Author's own admission of the central difficulty**: Reuven Harrison (Tufin CTO) writes that a tool "should also minimize false-positives, i.e., reporting breaking-changes which, in practice, cannot break an application" ([Detecting breaking changes in OpenAPI specifications](https://reuvenharrison.medium.com/detecting-breaking-changes-in-openapi-specifications-df19971321c8)).
- GitHub Action `oasdiff/oasdiff-action` with `fail-on: ERR|WARN`. Note it uploads both specs (encrypted) to a server ([action repo](https://github.com/oasdiff/oasdiff-action)).
- Commercial split: CLI and Action free; approval workflow and audit trail are Pro ([oasdiff.com](https://www.oasdiff.com/)).

### openapi-diff (OpenAPITools, JVM) — healthier than its reputation

- 1,095 stars, 81 open issues, Apache-2.0, pushed 2026-07-28. Releases every 1–3 months: `2.1.7` 2026-01-26, `2.1.6` 2025-11-26, `2.1.5` 2025-11-03 ([repo](https://github.com/OpenAPITools/openapi-diff)).
- Binary compatible/incompatible classification, not a graded catalogue. CI flags `--fail-on-incompatible`, `--fail-on-changed`.
- **Documented structural false positive**: [issue #192](https://github.com/OpenAPITools/openapi-diff/issues/192), open since 2020-10-23 — splitting a schema into two with `allOf` reports a breaking change "even if the end structure is the same". A pure refactor trips the differ.
- Other tools sharing the name: **Azure/openapi-diff** (288 stars, 91 open issues, no tagged releases, ARM-spec-specific); **Sayi/swagger-diff** (291 stars, last push 2023-09-18, Swagger 1.x/2.0 only — stale).

### Optic — archived January 2026

- **`archived: true`. "This repository was archived by the owner on Jan 12, 2026."** README: "Optic Labs is now part of Atlassian" ([repo](https://github.com/opticdev/optic)). 1,535 stars, MIT.
- Acquisition confirmed by Atlassian 2024-04-29; Optic folded into Compass ([announcement](https://www.atlassian.com/blog/announcements/optic-acquisition)).
- npm `@useoptic/optic`: **37,135 downloads** for 2026-07-23→29 — an order of magnitude below Spectral and Redocly, on a dead codebase.
- The community asked directly and got no maintainer answer: [discussion #2860](https://github.com/opticdev/optic/discussions/2860), opened 2024-08-27.
- Its ruleset was real (operation removal, new required parameters, optional→required, type changes, enum narrowing, response property removal, status-code removal) with `optic diff --check` and `--severity info|warn|error` driving exit codes.
- **Its differentiator was spec-vs-traffic, not consumer-awareness** — `optic capture` verified the spec against recorded traffic and applied surgical patches ([verify docs](https://www.useoptic.com/docs/verify-openapi)). **Phil Sturgeon's limitation, which generalises to every traffic-derived spec:** "Optic cannot infer why, only what" ([apisyouwonthate.com](https://apisyouwonthate.com/blog/turn-http-traffic-into-openapi-with-optic/)).
- **This matters beyond Optic**: the best-funded, most-cited OpenAPI breaking-change product in the space is dead. Read either as insufficient demand or as an acquisition outcome — both readings are available from these sources.

### Spectral — a linter, structurally unable to do this

- 3,166 stars (highest here), 278 open issues, Apache-2.0, pushed 2026-07-30. **npm `@stoplight/spectral-cli`: 1,531,344 weekly downloads** — by far the most-installed tool in this survey.
- Release cadence is uneven: `v6.16.2` 2026-07-20 and `v6.16.1` 2026-06-30, then a **14-month gap** back to `v6.15.0` 2025-04-22 ([releases](https://api.github.com/repos/stoplightio/spectral/releases)).
- Self-described as "A flexible JSON/YAML linter" for OpenAPI 3.1/3.0/2.0, Arazzo and AsyncAPI. **It sees one document at a time.** Feature request [#1504 "Compare two Open API specifications"](https://github.com/stoplightio/spectral/issues/1504) (2021-02-14) is closed. **Unverified (partial):** the maintainer's stated rationale — that Spectral has no way to compare versions of a file — is snippet-sourced; the issue body did not render on fetch.
- **Consequence**: Spectral can enforce rules that make breaks *less likely* (require `deprecated` before removal, mandate `x-extensible-enum`, forbid `additionalProperties: false`) but cannot answer "did this PR remove a field". Any claim that Spectral catches breaking changes is a category error.
- Stoplight acquired by **SmartBear, announced 2023-08-22** ([Business Wire](https://www.businesswire.com/news/home/20230822053731/en/SmartBear-to-Acquire-Stoplight-to-Deliver-Industrys-Broadest-Portfolio-of-API-Development-Capabilities)); Spectral/Prism/Elements folded into SwaggerHub in 2024 ([Business Wire](https://www.businesswire.com/news/home/20240418107187/en/SmartBear-Integrates-API-Tools-to-Enhance-Design-Experience-for-Teams)).

### Redocly CLI — no breaking-change detection

- 1,491 stars, 169 open issues, MIT, pushed 2026-07-30. **npm `@redocly/cli`: 2,041,434 weekly downloads** — the highest here.
- **There is no `diff` or breaking-change command.** The full command surface is `preview, translate, eject, build-docs, bundle, generate-client, join, score, split, stats, lint, check-config, respect, generate-arazzo, drift, proxy, generate-spec, login, logout, push, push-status, completion` ([docs](https://redocly.com/docs/cli/commands)).
- Marketing for the hosted platform does claim "breaking-change detection on every pull request" ([redocly-cli page](https://redocly.com/redocly-cli)); the OSS command surface does not back it. **Unverified:** no Redocly breaking-change rule catalogue was located.

### pb33f openapi-changes — the OpenAPI 3.2 option

- 357 stars, 11 open issues, Apache-2.0, `v0.2.10` 2026-07-01. npm `@pb33f/openapi-changes`: 18,935 weekly. Repo description: "The world's most powerful OpenAPI breaking changes detector" ([repo](https://github.com/pb33f/openapi-changes)).
- **Unique capability**: diffs a single spec across git history — a full change timeline, not just A-vs-B.
- **Supports OpenAPI 3.0/3.1/3.2** — relevant because .NET 10 defaults to 3.1 and .NET 11 to 3.2 (§20).
- **Unverified:** neither README nor docs states exit-code behaviour or a total rule count. That is the gap versus oasdiff for a hard CI gate.

### Bump.sh — commercial, coarse but honest defaults

- Classifies rename/delete endpoint, rename/delete property, type change, optional→required, security-requirement changes — each qualified "unless it was deprecated before" ([change management docs](https://docs.bump.sh/help/api-change-management/)).
- `bump diff --fail-on-breaking`; **note the default**: "By default the command will always exit with a successful return code" ([CI docs](https://docs.bump.sh/help/continuous-integration/cli/)). npm `bump-cli`: 457,791 weekly.
- ~5 rule classes versus oasdiff's 212 — much coarser.

### Postman

- The often-cited breaking-change detector is a **2020 blog how-to** the author built against the Postman API, not a product feature, and it disclaims implementation coverage ([blog.postman.com](https://blog.postman.com/how-to-catch-breaking-changes-before-they-happen/)).
- The actual governance product is **Spectral-based linting**, Enterprise-only, enforced in Spec Hub and via the Postman CLI as a merge gate ([API governance](https://learning.postman.com/docs/api-governance/api-governance-overview/), [CLI governance](https://learning.postman.com/docs/postman-cli/postman-cli-governance)) — inheriting Spectral's single-document blindness.

### The critique of spec-diff-only, at full strength

- **Matt Fellows, the sharpest line**: "Your schema still passes. Your tests are green. But your spec is now a fiction." ([Schemas can be contracts](https://pactflow.io/blog/schemas-can-be-contracts/), 2026-03-25). He argues a schema "cannot do is verify that your running service continuously honours its own spec", and invokes Hyrum's Law — consumers "build on undocumented behaviours" and "rely on ordering you never promised".
- **Criteo measured it**: ~5% invalid calls against the published spec (§6).
- **The consumer-blindness problem, stated canonically**: "In some ways, an OpenAPI spec is a theoretical contract. It describes all the ways the API can be used, but doesn't describe how the API is actually used in practice." ([Speakeasy, Pact vs OpenAPI](https://www.speakeasy.com/blog/pact-vs-openapi/)).
- **No tool in this survey does consumer-aware or traffic-informed diffing.** oasdiff has no per-consumer feature; Optic's traffic feature pointed the *other* way (spec accuracy). The mitigations are global policy knobs only: per-check severity overrides and `--deprecation-days`.
- **Evidence that false positives bite in practice**: a public repo reports "387 suppressed breaking changes in oasdiff" ([cal.diy issue #28754](https://github.com/calcom/cal.diy/issues/28754)). Mass suppression is the observed coping mechanism.
- **The counter-counter**: contract testing has the symmetric flaw. Spec-diff **over**-reports (flags changes no consumer uses); consumer contracts **under**-report (miss interactions the consumer never recorded). Neither is a superset of the other.
- **The counter-argument for spec-diff**: lower setup, no cross-team coordination, and responsibility aligned with the team that owns the spec ([Speakeasy](https://www.speakeasy.com/blog/pact-vs-openapi/)).

### Semantic breaks: no tool here detects them, and oasdiff says so
The cleanest citation is oasdiff's own docs (above). Concretely invisible to every differ in this section: a field keeps its type but changes units or timezone; pagination default changes; an enum member's *meaning* shifts; sort order changes; idempotency is dropped; a 200-with-error-body becomes a real 4xx; a synchronous write becomes eventually consistent. All wire-compatible, all breaking.

---

## 8. Generated clients as a compile-time contract

The consumer commits the provider's OpenAPI document, generates a typed client from it at build time, and a contract change surfaces as a **compile error**.

### The mechanism is real and it is build-integrated

- `NSwag.ApiDescription.Client` layers on Microsoft's generator-agnostic glue, `Microsoft.Extensions.ApiDescription.Client`, described as "MSBuild glue for OpenAPI code generation" ([README](https://github.com/dotnet/aspnetcore/blob/main/src/Tools/Extensions.ApiDescription.Client/README.md)).
- **It is wired into compile, not a scaffold**: `<Target Name="_TieInGenerateOpenApiCode" BeforeTargets="BeforeCompile" … DependsOnTargets="GenerateOpenApiCode" />` ([targets](https://github.com/dotnet/aspnetcore/blob/main/src/Tools/Extensions.ApiDescription.Client/src/build/Microsoft.Extensions.ApiDescription.Client.targets)).
- **It is incremental on the committed spec's timestamp** — `Inputs="@(OpenApiReference)"` — and hard-fails if the spec is missing: `<Error Condition="!Exists('%(OpenApiReference.FullPath)')" …>`. Touch the committed `.json`, the next build regenerates and recompiles.
- Item metadata: `ClassName`, `Namespace`, `CodeGenerator`, `Options` ([OpenApiItemsSchema.xaml](https://github.com/dotnet/aspnetcore/blob/main/src/Tools/Extensions.ApiDescription.Client/src/build/OpenApiItemsSchema.xaml)). `<Options>` is a raw passthrough of arbitrary NSwag CLI switches.

### What a compile error actually proves

**Only that the generated client's type surface is consistent with the spec file present at build time.** Nothing about the running provider.

What it does **not** prove, with sources:

| Miss | Evidence |
|---|---|
| **Added enum value** | NSwag clients throw at deserialization: "Error converting value 'Best' to type FriendlyStatus", and "NSwag doesn't allow you to customize or replace converters" ([Filip Kovář](https://filx.medium.com/how-to-fix-nswag-api-client-unknown-enum-value-error-a7faa0320012)). openapi-generator's Java target silently maps unknown values to `null`, which the proposal to change it calls "not the behavior expected in most of the cases" ([issue #625](https://github.com/OpenAPITools/openapi-generator/issues/625)); same hole in [PHP](https://github.com/OpenAPITools/openapi-generator/issues/20593) and [Kotlin](https://github.com/OpenAPITools/openapi-generator/issues/12970) |
| **Nullability change** | Microsoft: "There's no runtime difference between a non-nullable reference type and a nullable reference type" and annotations "don't introduce behavior changes" ([NRT docs](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/nullable-reference-types)). A field becoming nullable server-side produces zero compile errors |
| **Semantic change at identical shape** | "A provider may change the format of a field … from a string to an integer … could have catastrophic effects for the consumer" ([Speakeasy](https://www.speakeasy.com/blog/contract-testing-with-openapi/)); "it's easy to check if a system is compatible with a schema, but it's very difficult to be sure it fully implements the spec" ([Fellows](https://pactflow.io/blog/schemas-are-not-contracts/)) |
| **Side effects** | "A contract test does not check for side effects" ([docs.pact.io](https://docs.pact.io/consumer/contract_tests_not_functional_tests)) |
| **Provider actually serves that spec** | Criteo's ~5% invalid-response measurement (§6) |
| **Status codes, headers/auth, changed defaults, tightened validation** | **Unverified:** no on-point generated-client citation found for these four. The nearest evidence is that codegen bakes spec-derived client-side validation into generated code ([swagger-codegen #2549](https://github.com/swagger-api/swagger-codegen/issues/2549)), so a server-side tightening that is not regenerated goes uncaught — inference, not a documented incident |

### The critique of generated clients

- **Generator churn.** Kiota stamped a version into every generated file: "Now an update of the generated files, touches and modifies every file. Good luck spotting and verifying real changes from this noise" ([kiota #5489](https://github.com/microsoft/kiota/issues/5489)). NSwag once "duplicated the same client codes 20 times in the generated C# file" ([#5009](https://github.com/RicoSuter/NSwag/issues/5009)); method names changed purely from switching OpenAPI 2 → 3 ([#2578](https://github.com/RicoSuter/NSwag/issues/2578)).
- **Breaking regeneration from unrelated edits.** Omitting an unrelated `<ClassName>` lets Visual Studio's default `swaggerClient` collide with NSwag's `MultipleClientsFromOperationId`, silently merging unrelated controllers into one class and breaking compilation ([aspnetcore #55556](https://github.com/dotnet/aspnetcore/issues/55556)).
- **Numeric-suffix renumbering is a cross-toolchain hazard**: openapi-generator emits `EntityMapping1`/`EntityMapping2` ([#471](https://github.com/OpenAPITools/openapi-generator/issues/471)); ASP.NET Core's own generator does it for reused generic wrappers ([#62508](https://github.com/dotnet/aspnetcore/issues/62508)). See §20 for the .NET-specific version of this failure, which is worse than it looks.
- **File size and build cost.** An NSwag client exceeding 32,000 lines with "a substantial negative performance impact on our webpack build time", still open ([#1356](https://github.com/RicoSuter/NSwag/issues/1356)). Kiota against the ~2,200-discriminator Graph Beta spec went from under 13 minutes to blowing a 1-hour CI timeout ([#7401](https://github.com/microsoft/kiota/issues/7401)).
- **Nullable regressions ship.** NSwag `v14.7.0` (2026-04-08) dropped nullability markers on nullable `required` DTO properties; the release notes tell users to skip it ([releases](https://api.github.com/repos/RicoSuter/NSwag/releases)). Reporter's framing: "there is a difference between required and being nullable" ([#5359](https://github.com/RicoSuter/NSwag/issues/5359)).
- **Fowler warned about exactly this in 2011**: schema-driven code generation breaks on field additions that ought not to be breaking ([TolerantReader](https://martinfowler.com/bliki/TolerantReader.html)).

---

## 9. GraphQL schema checks — the mature usage-aware registry, and the contrast that matters

Not adoptable for a REST service, but it is the existence proof for what a sync-API registry *can* do, and the reason REST cannot copy it is mechanical.

- **Apollo GraphOS** runs build, operations and linter checks by default ([schema checks](https://www.apollographql.com/docs/graphos/platform/schema-management/checks)).
- **The key predicate**: "Operations checks use your graph's historical client operation data to determine whether any clients would be negatively affected by the proposed schema changes" (same URL). A field removal is breaking **iff observed production traffic touches it** — a fundamentally stronger predicate than Confluent's or Buf's purely structural ones.
- **Hard limits worth citing**: "Operations checks run against a maximum of 10,000 distinct operations"; default window "Within the last week"; knobs for operation-count threshold, excluded clients and excluded operations; and an explicit option to ignore breaking changes when the check runs against zero operations ([configure](https://www.apollographql.com/docs/graphos/platform/schema-management/checks/configure)) — which is also the obvious false-negative hole.
- **CI blocking is concrete**: "The `rover subgraph check` command returns a non-zero result if any check fails", plus PR status checks via a GitHub webhook ([run checks](https://www.apollographql.com/docs/graphos/platform/schema-management/checks/run)).
- **GraphQL Hive** goes further with **percentage thresholds**: mark breaking changes safe "based on real-life data and traffic reported to Hive" — 0% (breaking if used once), 10% of traffic, or 10 total operations ([targets docs](https://the-guild.dev/graphql/hive/docs/schema-registry/management/targets)). It rejects the publish: "If a non-safe change has been introduced in the schema check, it will be rejected by Hive" ([schema registry](https://the-guild.dev/graphql/hive/docs/schema-registry)).
- Repo note: `kamilkisiela/graphql-hive` and `graphql-hive/platform` both now redirect to [`graphql-hive/console`](https://github.com/graphql-hive/console) — **483 stars**, MIT, 263 open issues. Modest, despite the model's sophistication.

**Why REST cannot copy this.** Four mechanics, each individually citable; the synthesis is inference:

1. **One schema, introspectable in-band** — "A GraphQL service supports introspection over its schema, which is queried using GraphQL itself" ([graphql.org](https://graphql.org/learn/introspection/), [spec §4](https://github.com/graphql/graphql-spec/blob/main/spec/Section%204%20--%20Introspection.md)). OpenAPI is an out-of-band artifact that can drift.
2. **Mandatory field selection.** Every GraphQL request enumerates the fields it wants, so field-level usage telemetry is free. `GET /orders/42` returning JSON **does not tell you which properties the client read** — so the Apollo/Hive predicate is not computable from HTTP traffic without instrumenting every client. This is the decisive asymmetry.
3. **No URL/method/status surface** — the contract is entirely the type system.
4. **Unverified:** no named author was found making this comparative argument explicitly.

---

## 10. IDL-first: Protobuf, Buf, and Protobuf-over-HTTP

- **`buf breaking` rule categories** form a strictness ladder FILE → PACKAGE → WIRE_JSON → WIRE ([rules](https://buf.build/docs/breaking/rules/)): FILE "Detects changes that move generated code between files"; WIRE_JSON "Detects changes that break wire (binary) or JSON encoding … the recommended minimum level"; WIRE binary only. Guidance: "Pick one and stick with it".
- **Buf's actual stance is a layering argument, not a manifesto**: "Adding a new field is safe at every layer. Renaming an existing field breaks generated source code but leaves the wire format intact. Changing a field's type breaks everything." ([breaking overview](https://buf.build/docs/breaking/)). **The refusal to pick one granularity is itself the position**: "breaking" is a function of which layer your consumers depend on.
- **Baseline is flexible** — "a BSR module, a Git repository, a tarball, or a Buf image", canonically `buf breaking --against '.git#branch=main'`.
- **Exit code 100** on breaking changes, distinct from `1` for tool errors — a machine-distinguishable "your schema is incompatible" (verified against source via DeepWiki on `bufbuild/buf`).
- **The BSR rejects the publish — the strongest such evidence in this survey**: "Review flow off: the push is rejected outright with the check error", and server policy wins: "If `buf.yaml` disables breaking-change checks entirely, the BSR still enforces its policy" ([BSR breaking checks](https://buf.build/docs/bsr/checks/breaking/)). Escape hatch: unstable-package patterns (`v1alpha1`, `v1beta1`).
- Repo: 11,309 stars, Apache-2.0, 60 open issues, `v1.72.0` 2026-07-17, **554,951 asset downloads for that release alone**. **No license change detected** — Apache-2.0, and no relicense event could be confirmed.
- **Buf does not do OpenAPI breaking-change detection.** Searches of buf.build docs and blog surfaced no OpenAPI product. **This is established by absence**, not by an explicit disclaimer.
- **Protobuf-over-HTTP is real but partial.** Connect is HTTP-native — works over HTTP/1.1 and HTTP/2, avoids trailers, 16 error codes with fixed HTTP-status mapping ([protocol](https://connectrpc.com/docs/protocol/)); `connect-go` 4,014 stars. grpc-gateway maps request fields to URL path/query/body via `google.api.http` — 19,964 stars, pushed 2026-07-30 ([repo](https://github.com/grpc-ecosystem/grpc-gateway)).
- **What Protobuf-over-HTTP does *not* cover**: status-code semantics are lossy (gRPC↔HTTP codes are not 1:1; `UNAVAILABLE` and 503 are not always equivalent), header mapping needs explicit configuration and custom headers can be dropped, and the transcoder expands missing fields into defaults, **hiding** exactly the drift you wanted detected (practitioner sources: [1](https://medium.com/@umutt.akbulut/the-hidden-costs-of-grpc-rest-transcoding-701c2810142c), [2](https://shahbhat.medium.com/bridging-http-and-grpc-a-standardized-approach-to-header-mapping-in-microservices-382274748303) — secondary; the `HttpRule` scope itself is vendor-documented).
- **Net**: rigorous detection on the payload and method name, nothing on status codes, headers, caching, content negotiation or URL semantics. The §22 asymmetry reproduced *inside* HTTP — the IDL governs only the part of the surface it can see.
- **.NET**: `Grpc.Tools` **345.3M downloads**, `2.83.0` 2026-07-23. `protobuf-net.Grpc` **20.5M downloads**, latest stable `1.2.2` **2024-10-14** (~21 months). buf runs in a dotnet CI without Go tooling — standalone binary, [npm `@bufbuild/buf`](https://www.npmjs.com/package/@bufbuild/buf), Docker image, or [`bufbuild/buf-action`](https://github.com/bufbuild/buf-action).

---

## 11. Shared typed contract packages (a NuGet of DTOs, provider → consumer)

- **The case for**: one definition, compile-time safety, no codegen step, no spec artifact to keep in sync.
- **The case against, mechanically**: a NuGet upgrade is a **consumer-initiated pull**. The consumer chooses when to take the break, so detection is deferred to whenever they upgrade — and **production skew is entirely unaffected**. The package tells you nothing about what is deployed.
- **Fowler's framing is the sharpest lens**: the published/public distinction "matters more" than public/private, and non-published interfaces are cheap to change because refactoring tools reach every caller ([PublishedInterface](https://martinfowler.com/bliki/PublishedInterface.html)). A DTO package published across a bounded-context boundary *is* a published interface, with none of the reach.
- **Buf's BSR does the legitimate version of this** — it publishes *generated* SDKs from a schema that is itself gated (§10). The artifact is downstream of an enforced contract, not a substitute for one.
- **Zalando's scoping rule cuts against DTO sharing as a detection strategy**: its compatibility guarantee covers the wire format only and explicitly excludes source/binary compatibility of generated client code ([#106](https://opensource.zalando.com/restful-api-guidelines/#106)).
- **Unverified:** no primary source was reached for Sam Newman's specific position on shared libraries between services; his independent-deployability principle is available only in secondary renderings ([O'Reilly interview](https://www.oreilly.com/content/sam-newman-on-building-microservices/)).

---

## 12. Specification-driven test and mock generation

### Specmatic — the OpenAPI document *is* the contract

- 392 stars, 75 open issues, MIT, Kotlin, pushed 2026-07-30. Weekly releases: `2.51.0` 2026-07-25 ([repo](https://github.com/specmatic/specmatic)).
- **JVM at the core** — the only release assets are `specmatic.jar` (~85 MB) plus a checksum. Runs via `java -jar`, [Docker](https://hub.docker.com/r/specmatic/specmatic), or an npm wrapper (`npx specmatic`).
- Does **both** provider verification (contract-as-test) and consumer stubbing.
- **`backward-compatibility-check` mechanism is genuinely clever**: start a stub from the *new* spec, generate tests from the *previous* spec, run those against the new stub. Exits **1** on incompatibility; `--base-branch`, `--repo-dir`; HTML report ([docs](https://docs.specmatic.io/contract_driven_development/backward_compatibility)).
- **Licensing: backward-compatibility-check is in the free/OSS tier.** OSS = OpenAPI/REST + WSDL + MCP with contract testing, resilience testing, virtualization and backward-compat. Enterprise adds *protocols* (AsyncAPI/Kafka, gRPC, GraphQL, Avro) plus Studio/Insights; $50/user/mo at 50–250 seats ([pricing](https://specmatic.io/pricing/)).
- **Its own scoping caveat**: "A Contract Test is concerned with checking the APIs signature, while API tests are concerned with checking the APIs logic", and response example values are not compared to actual values unless Matchers are added ([contract testing docs](https://docs.specmatic.io/contract_driven_development/contract_testing.html)).
- **.NET: zero first-party.** A NuGet search for "specmatic" returns "0 packages returned" ([nuget.org](https://www.nuget.org/packages?q=specmatic)). The integration path is black-box over the wire.

### Schemathesis — the family that closes the "spec ≠ code" hole

- 3,492 stars, **9 open issues**, MIT, Python, pushed 2026-07-29, `v4.24.3` 2026-07-25 ([repo](https://github.com/schemathesis/schemathesis)).
- Property-based tests generated from an OpenAPI/GraphQL schema. Bug classes, verbatim from its README: "500 errors that crash your API on edge case inputs"; "Schema violations where your API returns different data than documented"; "Validation bypasses where invalid data gets accepted"; "Stateful bugs where operations work individually but fail in realistic workflows".
- **Python on the host does not matter for a .NET CI**: [`schemathesis/action@v3`](https://github.com/schemathesis/action), Docker `schemathesis/schemathesis:stable`, or `uvx schemathesis run <url>` ([CI/CD guide](https://schemathesis.readthedocs.io/en/stable/guides/cicd/)).
- **Funding reality**: company founded 2021, **unfunded** ([Tracxn](https://tracxn.com/d/companies/schemathesis/__gI1Ck2b2m8RzJh5uOiPj0xexXXir12Bpefz-XrL3pu4)); Open Collective total **$433.42 since June 2021** ([opencollective](https://opencollective.com/schemathesis)). One maintainer, [Dmitry Dygalo](https://github.com/Stranger6667).
- **What it cannot detect**: anything the schema does not describe — business rules, cross-service workflow semantics, persistence side effects.

### Microcks — CNCF incubating, and a .NET Testcontainers module exists

- 2,003 stars, 73 open issues, Apache-2.0, pushed 2026-07-28.
- **"Microcks was accepted to CNCF on June 22, 2023 and moved to the Incubating maturity level on May 3, 2026"** ([CNCF](https://www.cncf.io/projects/microcks/)) — incubating, not sandbox.
- Mocks plus conformance testing from OpenAPI/AsyncAPI/gRPC/GraphQL/Postman. Testcontainers modules for Java, Node/TS, Go **and .NET** ([modules](https://microcks.io/documentation/references/testcontainers-modules/)).
- **The .NET module is real but tiny**: [`microcks-testcontainers-dotnet`](https://github.com/microcks/microcks-testcontainers-dotnet) 11 stars; NuGet `Microcks.Testcontainers` v0.3.4, **27,380 downloads**. Sub-1.0, with feature gaps.
- **Unverified:** the last GA release — `/releases/latest` surfaced `1.15.0-rc1` (2026-06-22), a pre-release.

### Dredd — dead
`archived: true`, 4,225 stars, 260 open issues, last commit **2023-07-18**, last release `14.1.0` **2021-11-16** ([repo](https://github.com/apiaryio/dredd)). Fellows names Dredd's archival as a genuine ecosystem gap on the provider-verification side ([Schemas can be contracts](https://pactflow.io/blog/schemas-can-be-contracts/)).

### Postman-collection-based
[Portman](https://github.com/apideck-libraries/portman) (686 stars) generates a Postman collection plus tests from OpenAPI, run via [Newman](https://github.com/postmanlabs/newman) (7,239 stars). Node machinery, and the collection JSON becomes a second artifact to maintain.

---

## 13. Runtime / production contract monitoring, and traffic-diff testing

### Observed-traffic monitoring
- **Akita → Postman.** Acquired 2023-07-19; Abhinav Asthana described Akita as software that "automatically discovers and monitors APIs simply by watching API traffic", added as "eBPF-based API discovery" ([announcement](https://blog.postman.com/postman-acquires-akita-for-automated-api-observability/)).
- **The successor is still beta three years later** — "The Postman Insights beta works for first-party REST APIs" ([docs](https://learning.postman.com/docs/insights/overview)). The agent repo has **16 stars**, 4 open issues ([repo](https://github.com/postmanlabs/postman-insights-agent)). No sunset notice found.
- **APIClarity: archived.** 576 stars, last push 2024-10-08 ([repo](https://github.com/openclarity/apiclarity)).
- **Ecosystem-survival asymmetry worth naming**: two archivals (Optic 2026-01-12, APIClarity) and one paused project (Kusk, 2023-02) in the traffic/runtime family, versus zero archivals in the spec-driven family (§12).

### Traffic shadowing and response diffing — a family the standard taxonomies omit
- **Diffy** (originally Twitter, now maintained by Sn126) proxies each request to three instances — candidate, primary, secondary — and cancels noise statistically: "Diffy measures how often primary and secondary disagree with each other vs. how often primary and candidate disagree. If those rates are comparable, Diffy concludes the candidate has no real regression" ([repo](https://github.com/opendiffy/diffy), 1.5k stars).
- **Mixpanel built the same thing in Go** — "Miffy", an HTTP proxy that "accepts HTTP requests and forwards them to containers running production and candidate code", comparing both HTTP responses and Pub/Sub payloads. Matthew Hoare reports nondeterminism in message bundling as the main obstacle: "Container 1 could write events 1–3 in one message and 4–5 in another. Container 2 could split them differently." Outcome claimed is modest — it "has made our API deploys a lot safer than before" ([Mixpanel Engineering](https://medium.com/mixpaneleng/regression-testing-with-production-traffic-at-mixpanel-fc424eec4401)).
- **Christian Posta names the two disqualifying limitations.** On side effects: "If our services mutates data in our collaborators, we need to make sure those calls get directed to test doubles and not the real production traffic." On comparison noise: "you may get a lot of false positives in response comparisons because the data in the test cluster is using test data while the live services are using production data." And on the workaround's fragility — synthetic transactions are "implemented by convention and difficult to enforce" ([Advanced traffic-shadowing patterns](https://blog.christianposta.com/advanced-traffic-shadowing-patterns-for-microservices-with-istio-service-mesh/)).
- **What this family uniquely catches**: semantic and behavioural breaks (classes 6–7 in §1) that no schema-based approach can see, because it compares actual responses to actual responses. **What it costs**: a shadow environment, mutation isolation, and a noise-cancellation strategy.
- **Signadot** markets a sandbox variant, arguing "manual approaches are too brittle and time-consuming to scale" and that its system "automatically detects real API interactions between microservices during normal development workflows" ([Signadot](https://www.signadot.com/articles/stop-breaking-your-microservices-with-smarttests-ai-powered-contract-testing/)). Vendor material; it admits no limitations.

### Testing in production, and its limits
- Charity Majors' argument is about **scale and chaos**, not contract shape: "You can't spin up a copy of Facebook"; "You just can't usefully mimic the qualities of size and chaos" ([Honeycomb](https://www.honeycomb.io/blog/testing-in-production)). It should not be over-read as support for skipping a spec gate — a spec diff catches a class she is not talking about.
- **Progressive delivery** bounds blast radius rather than preventing the defect. **Unverified:** James Governor's originating RedMonk post 404'd on two attempts; the coinage and framing are secondary-sourced only.
- **The trade-off, stated plainly**: "Production becomes a quality gate instead of a delivery mechanism" ([contextqa](https://contextqa.com/blog/testing-in-production/)); runtime detection is strictly post-breach and the mitigation is exposure-limiting, not prevention.
- **The OTel finding is a negative one and it is decision-relevant.** No primary-authority writeup of detecting contract breaks from OpenTelemetry traces was located across multiple search framings. The nearest operational advice is to break error rates down "by endpoint, API consumer, and geographic region, since an overall 0.5% error rate might hide a 15% error rate for one specific consumer" ([Zuplo](https://zuplo.com/learning-center/api-observability-monitoring-complete-guide)), and the failure mode is named as "The API can be technically 'up,' but the contract changed" ([Redocly](https://redocly.com/learn/testing/contract-testing-101)). **Anyone listing OTel-based contract monitoring as an option is proposing to invent it, not adopt it.**

---

## 14. End-to-end integration suites spanning both services

### The critique is authoritative and two ThoughtWorks blips are on HOLD
- **"Broad integration tests" — HOLD, April 2024 (Vol 30)**: "The tests themselves are often fragile and unhelpful." ([radar entry](https://www.thoughtworks.com/radar/techniques/broad-integration-tests))
- **"Enterprise-wide integration test environments" — HOLD, October 2024**: they "invariably become a precious resource that's hard to replicate and a bottleneck", and "provide a false sense of security due to inevitable discrepancies in data" ([radar entry](https://www.thoughtworks.com/radar/techniques/enterprise-wide-integration-test-environments)). The prescription is ephemeral environments plus a dev-team-owned suite using fakes.
- **Fowler's narrow/broad distinction**: broad integration tests "require live versions of all services, requiring substantial test environment and network access" ([IntegrationTest](https://martinfowler.com/bliki/IntegrationTest.html)).
- **J.B. Rainsberger**, "Integrated Tests Are A Scam" (2009-04-05, updated 2023): "Unreliable, self-replicating time-wasters", giving a "false sense of security"; the arithmetic argument is that you write only a small fraction of the integrated tests thoroughness would require ([post](https://blog.thecodewhisperer.com/permalink/integrated-tests-are-a-scam), [talk](https://vimeo.com/80533536)). **Unverified:** the collaboration-tests-plus-contract-tests prescription is in the talk, not on that page.
- **Steve Smith**: "Test execution time and non-determinism are directly proportional to System Under Test scope"; "Non-deterministic tests can completely destroy the value of an automated regression suite"; "Any advantage you gain by talking to the real system is overwhelmed by the need to stamp out non-determinism" ([End-to-End Testing considered harmful](https://www.stevesmith.tech/blog/end-to-end-testing-considered-harmful/)). His alternative explicitly includes consumer-driven contracts.
- **Google's testing blog**: ~70/20/10 unit/integration/e2e; "Test runtime inflates significantly"; "Flakes and instability" ([Just Say No to More End-to-End Tests](https://testing.googleblog.com/2015/04/just-say-no-to-more-end-to-end-tests.html)).
- **Attribution correction**: "scam" is Rainsberger's word, "considered harmful" is Smith's. No such post by Thierry de Pauw was found; his relevant writing is [Don't let AI invert the testing pyramid](https://thinkinglabs.io/articles/2026/04/12/dont-let-ai-invert-the-testing-pyramid.html).

### The defence — best stated by the contract-testing vendor
- **PactFlow concedes what contract tests cannot do**: contract tests are "completed in isolation" and do not catch side effects or "the behavior of the overall system" — e.g. whether data actually persisted ([Contract testing vs integration testing](https://pactflow.io/blog/contract-testing-vs-integration-testing/)). That is the e2e case made by the party with every incentive not to make it.
- **Pact's own docs keep e2e**: they concede that a team demonstrating genuine value from e2e without substantial investment should keep them, and recommend e2e as **smoke tests** for critical scenarios pre-release ([convince me](https://docs.pact.io/faq/convinceme)).
- **Unverified:** no named "we deleted our contract tests and kept e2e" post was found.

---

## 15. Versioning discipline as a substitute for detection

If breaks are structurally prevented, detection is less load-bearing. Three sub-strategies, each with real institutional backing.

### Additive-only evolution + tolerant reader
- **Fowler's TolerantReader** (2011): invokes Postel, prescribes reading only what you need, making minimum structural assumptions, and encapsulating payload reading in one place ([bliki](https://martinfowler.com/bliki/TolerantReader.html)). **The under-cited half of his recommendation is consumer-driven contract testing**: share your reader code and tests *with the provider* so their build detects the break. Fowler's answer to tolerance is not "tolerate and hope".
- **Expand-contract / parallel change** — expand, migrate, contract ([ParallelChange](https://martinfowler.com/bliki/ParallelChange.html), by **Danilo Sato**, not Fowler; originated by Joshua Kerievsky). **ThoughtWorks Radar rates "API expand-contract" ADOPT** (April 2021) — and the blip names the prerequisite: the pattern requires coordination and visibility of API consumers, "perhaps through a technique such as consumer-driven contract testing" ([radar entry](https://www.thoughtworks.com/en-us/radar/techniques/api-expand-contract)). **The Adopt-ring restraint pattern arrives bundled with a pointer at the tooling.**
- **Zalando #108** mandates tolerant reading with four specific client obligations: tolerate unknown fields *without stripping them* from payloads needed for a later PUT; expect `x-extensible-enum` outputs to deliver new values; handle unspecified status codes by defaulting to the `x00` class; follow 301s ([#108](https://opensource.zalando.com/restful-api-guidelines/#108)).
- **Zalando #109 is the asymmetry, and it is the strongest institutional counter to naive tolerance**: servers should **reject** unknown *input* fields with 400. Zalando labels this "a specific deviation from Postel's Law", on three grounds — ignoring unknown input breaks PUT replace semantics, typo'd field names get silently swallowed, and future additions collide with previously-ignored fields ([#109](https://opensource.zalando.com/restful-api-guidelines/#109)). **Tolerant on the way out, strict on the way in.**

### Postel's law is contested — and the critique is now standards-blessed
- **RFC 1122 §1.2.2** states the robustness principle ([RFC](https://datatracker.ietf.org/doc/html/rfc1122)).
- Martin Thomson's [draft-thomson-postel-was-wrong](https://datatracker.ietf.org/doc/html/draft-thomson-postel-was-wrong) argued tolerating malformed input entrenches errors as de facto standards. **It was not abandoned — it became [RFC 9413, "Maintaining Robust Protocols"](https://www.rfc-editor.org/rfc/rfc9413.html)** (Thomson & Schinazi, June 2023, IAB Informational).
- RFC 9413's key sentences: "an interpretation that advocates for tolerating unexpected inputs is no longer considered best practice in all scenarios"; "negative consequences to interoperability accumulate over time if implementations silently accept faulty input"; and it names **"Virtuous Intolerance"** — "Choosing to generate fatal errors for unspecified conditions instead of attempting error recovery can ensure that faults receive attention."
- **Consequence**: citing Postel's law to justify doing nothing has a standards-track answer against it.
- Zalando reaches the same conclusion independently via Eric Allman's *The Robustness Principle Reconsidered*. **Unverified:** the CACM article text — cacm.acm.org, dl.acm.org and queue.acm.org all returned 403.

### Versioning + deprecation windows
- **Zalando #113 SHOULD avoid versioning** entirely; when unavoidable, prefer a new resource variant or a new endpoint over a parallel version in the same service ([#113](https://opensource.zalando.com/restful-api-guidelines/#113)).
- **Zalando #115 MUST NOT use URL versioning** — confirmed. Reasoning: the consumer must wait for a provider release+deploy; it is complex with hyperlinked dependencies; coordination across linked services is hard ([#115](https://opensource.zalando.com/restful-api-guidelines/#115)). Media-type versioning is mandated instead (#114).
- **Microsoft's older guideline permits URL-path *or* `?api-version=`**; **Azure's current guideline forbids a version segment in any operation path** and requires `api-version` as a query parameter on every operation ([Azure Guidelines](https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md)). Two Microsoft documents give opposite advice.
- **Headers**: [RFC 8594](https://www.rfc-editor.org/rfc/rfc8594.html) `Sunset` (May 2019, **Informational**); [RFC 9745](https://www.rfc-editor.org/rfc/rfc9745.html) `Deprecation` (March 2025, **Standards Track**), with `Sunset` MUST NOT precede `Deprecation`. The pair matured six years apart. Zalando #189 mandates them as a **SHOULD**; **Azure ignores them** in favour of a proprietary `azure-deprecating` header.
- **Published windows**:

| Provider | Window | Source |
|---|---|---|
| GitHub REST | previous version supported "at least 24 months"; notice promised but unquantified | [docs](https://docs.github.com/en/rest/about-the-rest-api/breaking-changes) |
| Azure GA | ~3 years | [devblogs](https://devblogs.microsoft.com/azure-sdk/azure-approach-to-versioning-and-avoiding-breaking-changes/) |
| Azure preview | 90 days; max 1 year in preview | [Azure Guidelines](https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md) |
| Google Cloud | ≥12 months' notice; Alpha/Beta excluded | [deprecation policy](https://cloud.google.com/terms/deprecation-20180816) |
| Google APIs (AIP) | no number; beta time-boxed ~90 days | [AIP-185](https://google.aip.dev/185), [AIP-181](https://google.aip.dev/181) |
| **Stripe** | **none published** | [versioning](https://docs.stripe.com/api/versioning), [upgrades](https://docs.stripe.com/upgrades) |

### Stripe: request-versioning instead of detection
- Date-based rolling versions with **per-account pinning**; major releases carry code names. Version observed on 2026-07-30: `2026-07-29.dahlia` ([versioning](https://docs.stripe.com/api/versioning)).
- Stripe's backward-compatible list is unusually permissive — new resources, new optional request parameters, new response properties, **reordering response properties**, and "changing the length or format of opaque strings … including adding or removing fixed prefixes" like `ch_` ([upgrades](https://docs.stripe.com/upgrades)). Your ID parser is explicitly not part of the contract.
- **Implementation**: one internal implementation at the current version, plus self-contained *version change modules* — documentation, a transformation function, and applicable resource types — walked backwards in time to the caller's pinned version; complex changes get a `has_side_effects` annotation that deliberately sacrifices encapsulation ([Brandur Leach](https://stripe.com/blog/api-versioning)).
- **What Stripe does instead of break detection**: a lightweight **API review process** before release. Human review plus a transformation layer, not a differ.
- **Critique**: Will Larson notes the approach reduces rather than eliminates multi-version overhead, and frames the residual cost as a deliberate retention trade — every version is more code to understand ([API deprecation strategy](https://lethain.com/api-deprecation-strategy/)).
- **Governance at scale is a human board, not a tool.** Azure runs a standing **Breaking Change Review Board** with weekly office hours that gates merges in `azure-rest-api-specs` behind an `Approved-BreakingChange` label ([review process](https://azure.github.io/azure-sdk/policies_reviewprocess.html)); breaking changes are not permitted after GA without an architecture-board exception.

---

## 16. "Do nothing more"

Represented at the strength its advocates give it; the counter-arguments are in §23.

- **YAGNI**: presumptive features carry cost of build, cost of delay, cost of carry and cost of repair ([Yagni](https://martinfowler.com/bliki/Yagni.html)). **The carve-out is where this case is weakest on Fowler's own authority**: Yagni "does not apply to effort to make the software easier to modify" — he lists refactoring, self-testing code and continuous delivery as practices that *support* Yagni. A break-detection gate is arguably in that second category.
- **Test pyramid**: broad tests are brittle, expensive, slow, prone to non-determinism, and belong as "a second line of test defense" ([TestPyramid](https://martinfowler.com/bliki/TestPyramid.html)). A cross-service gate sits at the top of the pyramid; this is the legitimate cost argument.
- **The monorepo argument**: unified versioning, atomic changes and large-scale refactoring are named benefits of a single repository ([Potvin & Levenberg, CACM 2016](https://research.google/pubs/why-google-stores-billions-of-lines-of-code-in-a-single-repository/)). **Unverified:** cacm.acm.org and the dl.acm.org PDF both 403'd; scale figures and the authors' "not for everyone" caveat are secondary-sourced.
- **The strongest evidence *against* the monorepo exemption is Pact's own not-good-for list** (§2). Monorepo, lockstep deployment and low service count appear nowhere on it. Pact's authors had every incentive and opportunity to concede that carve-out and did not.
- **The strongest evidence *for* doing it without tooling comes from ThoughtWorks' own CDC blip**, which reached Adopt three times: consumer-driven contracts are "a technique and an attitude that requires no special tool to implement", and "Writing Pact tests is not a guarantee that you are creating consumer-driven contracts" ([entry](https://www.thoughtworks.com/en-us/radar/techniques/consumer-driven-contract-testing)). ThoughtWorks' own teams later implemented CDC with **Ajv**, a JSON Schema validator, rather than Pact (Vol 29, Sep 2023, Trial).
- **Four large providers' published accounts support restraint-plus-something-else, not restraint alone** (§17): Stripe built a runtime version shim, Shopify built usage telemetry, Netflix built traffic replay, Uber built "a CI job". **None of the four adopted contract testing.** But none of the four did *nothing* either.
- **Unverified:** no source with standing publishes "monorepo ⇒ skip contract testing". The claim exists only in unattributed form.
- **Threshold arguments**: **Unverified.** No credible numeric threshold ("below N services, skip it") exists in the literature. Framing is consistently about independent deployability and team communication bandwidth. Anyone quoting a number is inventing it.
- The pragmatic middle that recurs: reserve consumer-driven contracts for high-criticality boundaries and use spec validation for the rest — roughly 80% of the value at 20% of the effort ([totalshiftleft](https://totalshiftleft.ai/blog/contract-testing-for-microservices)). **Caveat:** this claim surfaced in a search index but could not be located in the fetched article body.

---

## 17. Usage evidence — what is actually installed

Distinguish *most used* from *most recommended*. All figures 2026-07-30.

### NuGet (.NET)

| Package | Total downloads | Latest | Published |
|---|---|---|---|
| `Microsoft.OpenApi` | **1,528,429,932** | 3.9.0 | — |
| `Swashbuckle.AspNetCore` | **1,157,565,226** | 10.2.3 | 2026-06-22 |
| `Microsoft.Extensions.ApiDescription.Server` | **1,123,941,668** | 10.0.10 | 2026-07-14 |
| `Grpc.Tools` | 345,300,000 | 2.83.0 | 2026-07-23 |
| `Microsoft.AspNetCore.OpenApi` | **205,677,304** | 10.0.10 | 2026-07-14 |
| `Asp.Versioning.Http` | 137,200,000 | 10.0.x | see note |
| `Microsoft.Kiota.Abstractions` | **134,630,915** | 2.0.0 | 2026-05-06 |
| `NSwag.AspNetCore` | 132,164,765 | 14.7.1 | 2026-04-20 |
| `Microsoft.Kiota.Http.HttpClientLibrary` | 116,450,631 | 2.0.0 | — |
| `NSwag.MSBuild` | 97,321,670 | 14.7.1 | 2026-04-20 |
| `Swashbuckle.AspNetCore.Cli` | 45,950,557 | 10.2.3 | — |
| `NSwag.ApiDescription.Client` | 40,941,614 | 14.7.1 | 2026-04-20 |
| `WireMock.Net.OpenApiParser` | 33,893,304 | 2.13.0 | 2026-07-19 |
| `Verify.Xunit` | 21,600,000 *(deprecated)* | 31.12.5 | 2026-02-11 |
| `protobuf-net.Grpc` | 20,500,000 | 1.2.2 | **2024-10-14** |
| `NSwag.CodeGeneration.CSharp` | 19,864,930 | 14.7.1 | — |
| **`PactNet`** | **18,900,000** | **5.0.1** | **2025-03-22** |
| `Verify.XunitV3` | 3,819,374 | 31.27.0 | — |
| `PublicApiGenerator` | 4,800,000 | 11.5.4 | 2025-12-06 |
| `Microsoft.OpenApi.Kiota` (tool) | 4,194,704 | 1.34.1 | 2026-07-09 |
| `FluentAssertions.Web` | 3,000,000 | 2.0.3 | 2026-07-30 |
| **`Criteo.OpenApi.Comparator`** | **287,400** | **0.8.3** | **2025-11-13** |
| `Microcks.Testcontainers` | 27,380 | 0.3.4 | — |
| `openapi-tests` (tool) | **2,336** | 1.0.6 | 2026-05-07 |
| `Treaty` | **2,047** | 0.30.21 | 2025-12-11 |

Sources: the corresponding `nuget.org/packages/<id>` pages and the NuGet search endpoint `azuresearch-usnc.nuget.org/query?q=packageid:<id>`; `openapi-tests`/`Treaty`/`Criteo.OpenApi.Comparator` from [nuget.org search](https://www.nuget.org/packages?q=contract+testing+openapi) and [the package page](https://www.nuget.org/packages/Criteo.OpenApi.Comparator).

**What these numbers say:**

- **Describing an API outranks checking it by three orders of magnitude.** `Microsoft.OpenApi` at 1.5B and Swashbuckle at 1.16B versus PactNet at 18.9M and Criteo's comparator at 287K. **The .NET ecosystem overwhelmingly produces OpenAPI documents and overwhelmingly does not check them.**
- **PactNet's most-downloaded version is the deprecated 4.5.0 at 6,789,848** — larger than 5.0.1 (1,938,382) and 5.0.0 (1,739,867) combined. A large share of the .NET install base is on a version the docs mark deprecated.
- **There is no meaningfully-adopted .NET-native contract-testing tool besides PactNet.** `openapi-tests` (2,336) and `Treaty` (2,047) are the only candidates and are effectively unused.
- **Trap**: `Verify.Xunit`'s 21.6M is a **deprecated** package — its own NuGet page says it "has been deprecated as it is legacy and is no longer maintained", pointing to `Verify.XunitV3` ([page](https://www.nuget.org/packages/Verify.Xunit/)). Cite 3.82M, not 21.6M.
- **Note**: `Asp.Versioning.Http` release dates conflict between NuGet (10.0.0 → 2026-04-21) and GitHub Releases (2025-04-21). **Unverified** — confirm before citing the year.

### npm / Docker / release binaries

| Tool | Metric | Value |
|---|---|---|
| `@redocly/cli` | weekly npm | **2,041,434** |
| `@stoplight/spectral-cli` | weekly npm | **1,531,344** |
| `bump-cli` | weekly npm | 457,791 |
| `oasdiff` | Docker pulls (`tufin/oasdiff`) | **4,218,203** |
| `buf` | asset downloads, `v1.72.0` alone | **554,951** |
| `@useoptic/optic` | weekly npm | 37,135 *(archived project)* |
| `@pb33f/openapi-changes` | weekly npm | 18,935 |
| `oasdiff` | asset downloads, `v1.26.1` linux_amd64 | 10,480 |

**The ranking inverts the recommendation ranking.** The two most-installed tools — Redocly CLI and Spectral — are a docs/bundling toolchain and a linter. **Neither detects a breaking change.** The tool that actually does (oasdiff) has ~4.2M Docker pulls but a fraction of the npm-scale reach.

### What is actually wired into public CI

GitHub code search over `path:.github/workflows`:

| Invocation | Workflows |
|---|---|
| `buf breaking` | **1,332** |
| `redocly` | 1,296 |
| `spectral lint` | 926 |
| `oasdiff` | 394 |
| `schemathesis` | 383 |
| **`can-i-deploy`** (the Pact deploy gate) | **204** |
| `pact-broker` | 199 |
| `specmatic` | 98 |
| `openapi-changes` | 59 |

**Caveat**: counts are approximate and not strictly comparable — the `buf` and `redocly` totals include lint/generate/bundle uses unrelated to breaking-change detection. Directionally: **spec-diff and lint tooling outnumbers Pact's deploy gate by roughly 5×**, and the most-wired breaking-change command in public CI is `buf breaking` — a **Protobuf** tool that has never appeared on the ThoughtWorks Radar (§18).

**Two .NET data points that cut against the received wisdom:**

- **Snapshot testing is installed more than contract testing.** `Verify.Xunit` 21,582,598 vs `PactNet` 18,857,139; `WireMock.Net` (50,363,659) is **2.7×** PactNet; `Testcontainers` (98,743,813) is **5.2×**. PactNet by major: v4 8.74M, v5 4.21M, v3 2.31M, v2 2.93M.
- **Microsoft's own .NET reference microservices apps do not use Pact.** `PactNet` appears in **zero** `.csproj` files across [`dotnet/eShop`](https://github.com/dotnet/eShop), [`dotnet-architecture/eShopOnContainers`](https://github.com/dotnet-architecture/eShopOnContainers) and [`dotnet/eShopSupport`](https://github.com/dotnet/eShopSupport).

### GitHub activity and ecosystem survival

| Repo | Stars | Open issues | Latest release | Status |
|---|---|---|---|---|
| `OpenAPITools/openapi-generator` | 26,629 | **5,723** | v7.24.0 2026-07-20 | active |
| `grpc-ecosystem/grpc-gateway` | 19,964 | 153 | — | active |
| `bufbuild/buf` | 11,309 | 60 | v1.72.0 2026-07-17 | active |
| `RicoSuter/NSwag` | 7,351 | **2,051** | v14.7.1 2026-04-20 | single maintainer |
| `domaindrivendev/Swashbuckle.AspNetCore` | 5,496 | 176 | v10.2.3 2026-06-22 | **active — revived** |
| `asyncapi/spec` | 5,255 | 44 | — | active |
| `stoplightio/prism` | 4,994 | 148 | v5.16.0 2026-07-17 | active |
| `Azure/autorest` | 4,799 | 45 | — | **deprecated, retires 2026-07-01** |
| `apiaryio/dredd` | 4,225 | 260 | 14.1.0 2021-11-16 | **ARCHIVED** |
| `microsoft/kiota` | 3,786 | 227 | v1.34.1 2026-07-09 | very active |
| `schemathesis/schemathesis` | 3,492 | **9** | v4.24.3 2026-07-25 | active, unfunded |
| `stoplightio/spectral` | 3,166 | 278 | v6.16.2 2026-07-20 | active, uneven cadence |
| `microcks/microcks` | 2,003 | 73 | 1.15.0-rc1 | CNCF **incubating** |
| `pact-foundation/pact-js` | 1,794 | 128 | v17.0.1 2026-07-01 | **8 stable releases in 2026** |
| `RicoSuter/NJsonSchema` | 1,581 | 441 | — | active |
| `opticdev/optic` | 1,535 | 29 | v1.0.9 2025-08-10 | **ARCHIVED 2026-01-12** |
| `Redocly/redocly-cli` | 1,491 | 169 | — | active |
| `oasdiff/oasdiff` | 1,299 | 35 | v1.26.1 2026-07-27 | very active |
| `OpenAPITools/openapi-diff` | 1,095 | 81 | 2.1.7 2026-01-26 | active |
| **`pact-foundation/pact-net`** | **929** | **37** | **5.0.1 2025-03-22** | **see §20** |
| `Apicurio/apicurio-registry` | 875 | 485 | — | active |
| `spring-cloud/spring-cloud-contract` | 732 | 111 | v5.0.3 2026-06-11 | **ARCHIVED 2026-07** |
| `openclarity/apiclarity` | 576 | 54 | — | **ARCHIVED** |
| `graphql-hive/console` | 483 | 263 | — | active |
| `specmatic/specmatic` | 392 | 75 | 2.51.0 2026-07-25 | weekly releases |
| `pb33f/openapi-changes` | 357 | 11 | v0.2.10 2026-07-01 | active |
| `kubeshop/kusk-gateway` | 281 | 110 | — | **last push 2023-02** |
| `pact-foundation/pact-reference` | 103 | 46 | — | pushed **2026-07-30** |
| `asyncapi/diff` | 28 | — | 0.5.0 | minimal |
| `postmanlabs/postman-insights-agent` | 16 | 4 | — | still beta |
| `criteo/openapi-comparator` | 36 | 6 | 0.8.3 2025-11-13 | small, alive |
| `stubborn-sh/stubborn-contract` | **5** | — | — | Spring Cloud Contract successor |

**Four archivals or de facto deaths in this space** — Optic (Jan 2026), Dredd, APIClarity, Spring Cloud Contract (Jul 2026) — plus AutoRest deprecated and Kusk paused. **That is a high mortality rate for a category the industry says is important.**

**Unverified:** `atlassian/swagger-request-validator` now redirects to `atlassian/openapi-request-validator` with `created_at: 2025-01-28` and 12 stars, inconsistent with a plain rename of a long-standing ~1k-star project. The star figure is not comparable to the tool's real install base; check Maven Central instead.

### Named engineering accounts — what large providers actually built

**None of the four biggest published accounts uses contract testing. Three of them reached for telemetry or traffic replay instead.**

- **Stripe** — accounts are **pinned** on first request; responses are generated at the current version then walked *backward* through composable version-change modules. Compatibility claimed with every version since 2011 across ~100 breaking changes. **No contract tests, no schema diff — a runtime compatibility shim** ([Brandur Leach, 2017-08-05](https://stripe.com/blog/api-versioning)).
- **Shopify** — quarterly date-named versions, and detection is **telemetry, not static analysis**: `mark_breaking` / `mark_possibly_breaking` annotate call sites and emit into their Monorail warehouse, so teams run "impact assessment reports" to size the blast radius before removing anything. Their stated policy: "Break the API contract with the ecosystem only when there are no alternatives." ([Tom Newton, 2019-12-17](https://shopify.engineering/shopify-manages-api-versioning-breaking-changes)).
- **Netflix** — **replayed production traffic** against both the old and new implementations and diffed flattened JSON node paths. It caught classes no schema diff can see: `/data/videos/0/tags/3/id: (81496962, null)`, encoding drift (`Série`), and "differences in localization, date precisions, and floating point accuracy" ([Migrating Netflix to GraphQL Safely](https://netflixtechblog.com/migrating-netflix-to-graphql-safely-8e1e4d4f1e72), 2023-06-14). This is §13's traffic-diff family, at scale, by name.
- **Uber** — backward-incompatible endpoint-schema changes are "prevented by a CI job" ([Architecture of Uber's API gateway](https://www.uber.com/blog/architecture-api-gateway/), 2021-05-19). That single sentence is the entire treatment; cite it as evidence of practice, not of mechanism.
- **GitHub** — policy lives in docs, not a blog post. GraphQL: ≥3 months' notice, quarter boundaries only, and a two-tier **breaking** vs **dangerous** taxonomy ([GraphQL breaking changes](https://docs.github.com/en/graphql/overview/breaking-changes)). REST: date versions via `X-GitHub-Api-Version`, old versions supported "at least 24 more months", then `410 Gone` ([REST API versions](https://docs.github.com/en/rest/about-the-rest-api/api-versions)).

**The honest ceiling of static spec diffing, stated by a company that built one.** Yelp's [`swagger-spec-compatibility`](https://github.com/Yelp/swagger-spec-compatibility) ships 9 rules (`REQ-E001..E004`, `RES-E001..E003`, …) and says outright that it "is not supposed to cover all the possible cases of backward incompatibility" — rules exist only for breaking changes "we've experienced internally at Yelp." **That is why Shopify and Netflix reached for telemetry and replay.** The project is abandoned in practice: 20 stars, last substantive commits 2022-12.

**API guidelines are widely published; their *executable* rulesets govern style, not evolution.** Two falsifiable checks:

- **adidas** ships a real 275-line Spectral ruleset, but grepping it for `breaking|compatib|deprecat|version` returns **zero matches** — all 17 rules are naming, format and security conventions ([adidas/api-guidelines](https://github.com/adidas/api-guidelines)). **Anyone citing it as breaking-change tooling is wrong.**
- **Otto** has the richest compatibility *prose* of the set — `must-not-break-backward-compatibility`, `must-monitor-usage-of-deprecated-api-scheduled-for-sunset`, `must-obtain-approval-of-consumers-before-api-shutdown`, with separate answers for data vs domain events on Kafka — but its executable Redocly ruleset (~22 rules) is convention-only ([otto-de/api-guidelines](https://github.com/otto-de/api-guidelines)).
- **Zalando: the ruleset stuck, the linter stalled.** [`zalando/zally`](https://github.com/zalando/zally) has 945 stars, no deprecation notice and `archived: false`, but its last release is **v2.1.1, 2022-12-09** (3y7m) with **one commit in the trailing 12 months**, 9 of the last 12 commits being dependabot npm bumps in `/web-ui`, and a README build badge pointing at the wrong repo. Meanwhile [`restful-api-guidelines`](https://github.com/zalando/restful-api-guidelines) (3,220 stars) is actively evolving on exactly this topic — including "Clearly separate compatibility rules for input and output schemas (#851)" and deprecating `x-extensible-enum`, both 2025-11-27. **The written rules outlived the tool that enforced them.**

**Two assigned hypotheses that do not hold:**

- **Spotify's Golden Paths post never mentions APIs, contracts or breaking changes** ([post](https://engineering.atspotify.com/2020/08/how-we-use-golden-paths-to-solve-fragmentation-in-our-software-ecosystem), 2020-08-17).
- **Backstage's `kind: API` descriptor stores and renders only** — no compatibility validation, no version comparison, and nothing gates a change ([descriptor format](https://backstage.io/docs/features/software-catalog/descriptor-format/)). It makes consumers *discoverable* for deprecation planning; that is the whole contribution.

**Unverified:** SoundCloud's CDC/BFF-era writing, the REA Group / DiUS account of Pact's origin, and named accounts from Monzo, Atlassian, Slack, Twilio, Wise, Booking.com, Etsy, Deliveroo, Skyscanner, Klarna, N26, ING and IKEA. These were searched through a degraded channel after the WebSearch budget was exhausted; "not found" here means not found, **not** that they do not exist.

**Method trap worth carrying forward**: GitHub's `updated_at` is misleading for maintenance judgements — Zally's `updated_at` reads 2026-07-25 against a real `pushed_at` of 2026-07-08 and one commit all year. Use `pushed_at` plus the commit list.

---

## 18. ThoughtWorks Radar movement — the movement *is* the finding

**Method note.** Individual blip pages are JavaScript-rendered and `/radar/<quadrant>/<slug>` **silently 404s to the current volume listing** rather than erroring — one fetch of `/radar/tools/pact` returned an entirely different blip's content, and that result was discarded. The rings below were re-derived from `pdftotext` over **15 official volume PDFs, Vol 20 (Apr 2019) → Vol 34 (Apr 2026)**. Pre-2019 rings (Pact 2014–15, CDC 2015–16, APIs-as-a-product 2016–17) rest on blip pages alone and could not be PDF-verified.

| Blip | Quadrant | Appearances (ring) | Highest ever | Current |
|---|---|---|---|---|
| **Pact & Pacto** | Tools | Jul 2014 **Trial**, Jan 2015 **Trial** | **Trial** | **Absent** ([entry](https://www.thoughtworks.com/en-us/radar/tools/pact-pacto)) |
| **Consumer-driven contract testing** | Techniques | May 2015, Nov 2015, Nov 2016 — **Adopt** all three | **Adopt** | **Absent since Nov 2016** ([entry](https://www.thoughtworks.com/en-us/radar/techniques/consumer-driven-contract-testing)) |
| **Pactflow** | Tools | Oct 2021 **Assess**, Mar 2022 **Trial** | **Trial** | Absent ([entry](https://www.thoughtworks.com/en-us/radar/tools/pactflow)) |
| **Spectral** | Tools | Apr 2021 **Assess**, Oct 2022 **Trial** | **Trial** | Absent ([entry](https://www.thoughtworks.com/en-us/radar/tools/spectral)) |
| **API expand-contract** | Techniques | Apr 2021 **Adopt** | **Adopt** | Absent ([entry](https://www.thoughtworks.com/radar/techniques/api-expand-contract)) |
| **Apicurio Registry** | Tools | Apr 2023 **Trial** | Trial | Absent ([entry](https://www.thoughtworks.com/radar/tools/apicurio-registry)) |
| **AsyncAPI** | Tools | May 2020 **Assess** (once) | **Assess** | Absent ([entry](https://www.thoughtworks.com/radar/tools/asyncapi)) |
| **Backstage** | Platforms | Oct 2020 Assess → Apr 2021 Trial → Oct 2021 Trial → Oct 2022 **Adopt** | **Adopt** | Absent ([entry](https://www.thoughtworks.com/radar/platforms/backstage)) |
| **APIs as a product** | Techniques | Nov 2016, Mar 2017 — **Trial** | Trial | Absent ([entry](https://www.thoughtworks.com/radar/techniques/apis-as-a-product)) |
| **42Crunch API Conformance Scan** | Tools | Apr 2024 **Trial** | Trial | ([entry](https://www.thoughtworks.com/radar/tools/42crunch-api-conformance-scan)) |
| **Data Contract CLI** | Tools | Nov 2025 **Trial** | Trial | *data* contracts, not HTTP ([entry](https://www.thoughtworks.com/radar/tools/data-contract-cli)) |
| **Testcontainers** | Tools | Oct 2024 **Adopt** | **Adopt** | *Contrast: the fake-the-dependency approach did reach Adopt* |
| **Enterprise-wide integration test environments** | Techniques | Mar 2017, Nov 2017, **Oct 2024** — **HOLD** all three | — | **HOLD** ([entry](https://www.thoughtworks.com/en-us/radar/techniques/enterprise-wide-integration-test-environments)) |
| **Broad integration tests** | Techniques | Apr 2024 **HOLD** | — | **HOLD** ([entry](https://www.thoughtworks.com/radar/techniques/broad-integration-tests), [Vol 30 PDF](https://www.thoughtworks.com/content/dam/thoughtworks/documents/radar/2024/04/tr_technology_radar_vol_30_en.pdf)) |

**Never blipped at all** — zero occurrences across Vols 20–34, full-text verified: **Optic, Buf, Specmatic, Microcks, Schemathesis, oasdiff, openapi-diff, Redocly, Dredd, Stoplight, WireMock, Zally, Kiota, NSwag, Apollo Federation, PactFlow Bi-Directional Contract Testing, "Tolerant reader"**. (The lone "Buf" hit is the substring inside "Protocol Buffer"; the two "Optic" hits are "optical".)

### What the movement says

- **The *technique* outranked the *tool*, and both were retired.** CDC-as-technique reached **Adopt** three times (2015–16); Pact the tool never got past **Trial**. Nothing in this space was ever moved to **Hold** — they were simply dropped.
- **"Pact" appears zero times in Vols 27–34** — eight consecutive volumes, Oct 2022 → Apr 2026.
- **The CDC blip's own closing sentences are the most quotable restraint argument in this document**: it is "a technique and an attitude that requires no special tool to implement", and "Writing Pact tests is not a guarantee that you are creating consumer-driven contracts" ([entry](https://www.thoughtworks.com/en-us/radar/techniques/consumer-driven-contract-testing)).
- **ThoughtWorks flagged Pact's scaling cost in the Pactflow blip itself** (Mar 2022): "We've used Pact for contract testing long enough to see some of the complexity that comes with scale." ([entry](https://www.thoughtworks.com/en-us/radar/tools/pactflow)).
- **"backward compatib\*" appears six times in fifteen volumes**, and the only occurrence about API/schema compatibility *enforcement* is inside the Apicurio Registry blip (Apr 2023, Trial) — "enforce schema evolution restrictions, such as backward compatibility" — i.e. a **Kafka** schema registry, not an HTTP tool. §22's asymmetry shows up in the radar's own vocabulary.
- **In Vol 34 (Apr 2026) contract testing survives only as a foil**, inside the WuppieFuzz blip: teams "still rely on example-based integration and contract tests, which rarely probe unexpected inputs."
- **Spectral survives only as a dependency** of another blip — "Architecture drift reduction with LLMs" (Vol 34, Assess) names "deterministic analysis tools (such as Spectral, ArchUnit or Spring Modulith)".
- **ThoughtWorks' own teams did CDC without Pact**: the Ajv blip (Vol 29, Sep 2023, Trial) records that they "used Ajv for implementing consumer-driven contract testing in CI workflows."
- **.NET-relevant and current**: **ConfIT** (Vol 34 Tools, **Assess**) — declarative JSON API tests, described as worth assessing for .NET teams exploring specification-driven API testing, with the caveat of "limited community adoption and a small ecosystem."
- **What the two HOLD rings establish**: the alternative most often proposed *instead* of contract testing — a shared cross-service integration environment — is the thing ThoughtWorks tells you to stop doing, and the Oct 2024 HOLD blip **recommends contract testing as the alternative**. The Broad-integration-tests HOLD says "numerous organizations over-invested in what we believe to be ineffective broad integration tests", prescribing service virtualization *then* contract tests. Both HOLDs land independently on "false sense of security" — from environment fidelity and from path-coverage arithmetic respectively.

### Practitioner surveys

**Postman State of the API** — self-selecting vendor survey; Postman sells contract-testing templates.

| Metric | 2021 | 2022 (n=37,332) | 2023 (n≈40,000) | 2024 (n=5,600+) | 2025 (n=5,700+) |
|---|---|---|---|---|---|
| **Contract testing** | — | ≤58%\* | ≤57%\* | — | **17%** |
| Functional testing | — | 68% | 67% | — | 67% |
| Integration testing | — | 67% | 64% | — | 67% |
| Versioning APIs | — | 62% | — | — | 60% |
| **Semantic versioning** | 20% | 23% | — | — | **26%** |
| REST usage | — | — | — | — | 93% |

\*upper bound only — the 2022/2023 reports state no other practice came "within ten percentage points" of functional/integration testing.

- "contract testing lags at only 17%, a critical gap" ([2025 report p.23](https://voyager.postman.com/doc/postman-state-of-the-api-report-2025.pdf), [landing](https://www.postman.com/state-of-api/2025/)).
- "only 26% use semantic versioning, meaning most teams track changes without communicating the impact" (ibid. p.24).
- **Breaking-change failure rate, 2024**: "56% of API changes succeed with minimal issues, a concerning 5% experience failure rates above 25%" ([2024 report p.14](https://voyager.postman.com/doc/postman-state-of-the-api-report-2024.pdf)).
- Specification usage, 2022: JSON Schema **72%**, Swagger 2.0 **55%**, OpenAPI 3.x **39%** ([2022 report p.44](https://voyager.postman.com/doc/postman-state-of-the-api-2022.pdf)).
- API-first: 82% at "some level", **25% fully** in 2025; 74% in 2024, up from 66% in 2023 ([2023 report](https://voyager.postman.com/pdf/2023-state-of-the-api-report-postman.pdf)).
- **Two methodology warnings that materially weaken the 17%**: the sample collapsed ~7× from ~40,000 (2023) to 5,700 (2025), so the figure comes from a differently recruited panel; and **there is no prior-year contract-testing number to compare it against** — 2022/2023/2024 report none. The API-first question also changed wording between 2023 and 2024, so 11% → 74% is not a trend.

**The other surveys have nothing.** Verified absences:

- **Stack Overflow 2025** (49,000+ responses, 314 technologies): no API-tooling, OpenAPI, gRPC or contract-testing questions ([survey](https://survey.stackoverflow.co/2025/)).
- **JetBrains Developer Ecosystem 2025** (24,534 respondents): four sections only — AI, productivity, tools-and-trends, life-and-work. Nothing on APIs or testing ([site](https://devecosystem-2025.jetbrains.com/)).
- **CNCF Annual Survey 2024** (750 respondents): no contract-testing, API-testing or breaking-change figures ([report](https://www.cncf.io/reports/cncf-annual-survey-2024/)).
- **SmartBear State of Software Quality | API** (2023, "over 1,100 API practitioners") — **Unverified:** the page is a JS shell and no un-gated PDF was found at three candidate URLs. **Conflict of interest worth stating: SmartBear owns PactFlow** ([pactflow.io](https://pactflow.io/) footer).
- **Unverified:** Kong, Nordic APIs and Gartner figures — the session's WebSearch budget was exhausted before these could be searched.

---

## 19. Where credible people disagree, and why

| Position | Who | Root of the disagreement |
|---|---|---|
| A contract-test failure should start a conversation, not break the build | **Fowler**, [ContractTest](https://martinfowler.com/bliki/ContractTest.html) | Treats the contract as a shared social artifact between teams |
| A contract failure must hard-gate the deploy | **Pact**, [can-i-deploy](https://docs.pact.io/pact_broker/can_i_deploy) | Treats production version-skew as the risk to eliminate |
| Schemas are not contracts | **Fellows, 2024**, [post](https://pactflow.io/blog/schemas-are-not-contracts/) | A schema is satisfiable without being implemented |
| Schemas **can** be contracts, given enforcement | **Fellows, 2026**, [post](https://pactflow.io/blog/schemas-can-be-contracts/) | Conceded on cost grounds: "adoption by some teams was slow … because of the cost" |
| Be liberal in what you accept | **RFC 1122** | Assumes specifications are immutable |
| Tolerance is no longer best practice; prefer **virtuous intolerance** | **RFC 9413** (Thomson & Schinazi) | Assumes protocols are actively maintained; tolerance entrenches errors |
| Tolerate on output, **reject** on input | **Zalando #108 + #109** | PUT replace-semantics and silent typo-swallowing are concrete harms |
| Adding a response enum value is safe | **GitHub** | Consumers assumed to be tolerant readers |
| Extending an output enum is forbidden | **Zalando #107** | Consumers assumed to be strict deserializers |
| Never version in the URL | **Zalando #115** | Hyperlinked resources make coordinated version bumps intractable |
| Version in the URL path or query is required | **Microsoft (older)** | Explicit versioning as a governance requirement |
| No version segment in any operation path | **Azure (current)** | Same organisation, opposite conclusion |
| Avoid versioning; get the design right, review changes by hand | **Stripe / Azure review board** | At sufficient scale, human review outperforms a differ |
| Broad integration environments are HOLD | **ThoughtWorks** | Fidelity and bottleneck costs exceed the confidence gained |
| CDC "requires no special tool to implement" | **ThoughtWorks CDC blip** (Adopt ×3) | Separates the discipline from the tooling; the technique reached Adopt, the tool never left Trial |
| Detect via *usage telemetry*, not static analysis | **Shopify** (`mark_breaking`) | Only observed usage tells you the blast radius |
| Detect via *traffic replay + response diff* | **Netflix** | Semantic and encoding drift is invisible to any schema diff |
| Avoid detection entirely with a runtime version shim | **Stripe** | At their consumer count, per-account pinning beats coordination |
| Keep a small e2e smoke suite | **Pact's own FAQ** | Contract tests provably miss side effects |

**The four root causes, distilled:**

1. **Can you reach every caller?** Fowler's published-vs-public distinction ([PublishedInterface](https://martinfowler.com/bliki/PublishedInterface.html)) is the real axis. If refactoring tools reach all callers, most of this document is moot.
2. **Is the deploy atomic?** If not, version skew exists regardless of repo layout (§23).
3. **Is the consumer set knowable?** Pact requires yes; public APIs are its stated exclusion.
4. **Public vs internal.** Every guideline that forbids something (Zalando on output enums, AIP-180 on type changes) is written for a provider that cannot see or coordinate its consumers.

---

## 20. .NET / C# reality

### Build-time OpenAPI export — the producer side

| Mechanism | Status | Notes |
|---|---|---|
| `Microsoft.Extensions.ApiDescription.Server` | 1.12B downloads, `10.0.10` | Generator-agnostic MSBuild glue; predates the built-in generator |
| .NET 9+ built-in build-time generation | **introduced in .NET 9** | `Microsoft.AspNetCore.OpenApi` (runtime) + `Microsoft.Extensions.ApiDescription.Server` (build-time) ([docs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi?view=aspnetcore-10.0)) |
| `Swashbuckle.AspNetCore.Cli` | 46M downloads | `swagger tofile --output [out] [assembly] [doc]` ([docs](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/blob/master/docs/configure-and-customize-cli.md)) |
| NSwag `aspnetcore2openapi` | active | Wired `AfterTargets="Build"` — the *opposite* ordering from Microsoft's glue, which runs `BeforeTargets="Build"` ([wiki](https://github.com/RicoSuter/NSwag/wiki/NSwag.MSBuild)) |

**Mechanism, verified at source** ([targets](https://github.com/dotnet/aspnetcore/blob/main/src/Tools/Extensions.ApiDescription.Server/src/build/Microsoft.Extensions.ApiDescription.Server.targets), [props](https://github.com/dotnet/aspnetcore/blob/main/src/Tools/Extensions.ApiDescription.Server/src/build/Microsoft.Extensions.ApiDescription.Server.props)):

- `_GenerateOpenApiDocuments` runs `BeforeTargets="Build"`, invoking `dotnet-getdocument.dll`.
- Properties: `OpenApiGenerateDocuments` (default `true`), `OpenApiGenerateDocumentsOnBuild`, `OpenApiDocumentsDirectory` (**defaults to `obj/`**), `OpenApiGenerateDocumentsOptions`, `OpenApiGenerationEnvironment`.
- Option args go through `OpenApiGenerateDocumentsOptions`: `--file-name`, `--document-name`, `--openapi-version OpenApi3_1`.
- **The caveat that bites**: "Build-time OpenAPI document generation functions by launching the apps entrypoint with a mock server implementation… any logic in the apps startup is invoked". Your `Program.cs` runs during `dotnet build`; the documented guard is `if (Assembly.GetEntryAssembly()?.GetName().Name != "GetDocument.Insider")`.
- **YAML is not supported at build time.** Known sharp edges: host not disposed, can hang the build ([#43395](https://github.com/dotnet/aspnetcore/issues/43395)); linux-arm64 failures ([#65230](https://github.com/dotnet/aspnetcore/issues/65230)); silently no-ops if `Swashbuckle.AspNetCore.SwaggerGen` is also installed ([Q&A](https://learn.microsoft.com/en-us/answers/a/2022896)); progress invisible under Terminal Logger unless `dotnet build -tlp:v=d`.
- The docs list **"Committed into source control"** as the *first* reason to generate at build time — then never mention comparing the committed copy to a fresh one.

### 🔴 The staleness gate: no first-party mechanism exists. Verified at code level.

- **The tool has no verify/check/diff mode.** `dotnet-getdocument`'s complete option set is `--file-list, --output, --openapi-version, --document-name, --file-name, --environment` plus base options. **No `--verify`, `--check`, `--fail-on-diff`, or comparison input of any kind** ([GetDocumentCommand.cs](https://github.com/dotnet/aspnetcore/blob/main/src/Tools/GetDocumentInsider/src/Commands/GetDocumentCommand.cs)).
- **No MSBuild property offers it** — the `.props` exposes exactly five user-settable properties, none a verification switch.
- **The Learn docs never describe the pattern.** A grep of the full [ASP.NET Core OpenAPI docs page](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi?view=aspnetcore-10.0) for `continuous integration`, `CI pipeline`, `version control`, `compare`, `breaking` and `git diff` returned **zero matches**.
- **Neither does the launch blog.** Mike Kistler gets as close as: generating at build time "makes it much easier to integrate with tools in your local development workflow or CI pipeline", "you can run a linter on the generated document" — **linting and generating, never comparing** ([devblogs](https://devblogs.microsoft.com/dotnet/dotnet9-openapi/)).
- **The roadmap does not plan it.** [`captainsafia`'s meta-issue #58353](https://github.com/dotnet/aspnetcore/issues/58353) "Improve build-time OpenAPI document generation" (milestone .NET 12 Planning) has five items, **none about staleness verification**.
- **Closest first-party thing is a pattern Microsoft uses on itself but does not ship**: the ASP.NET Core team snapshot-tests its own generated documents with Verify against committed `*.verified.txt` files ([test dir](https://github.com/dotnet/aspnetcore/tree/main/src/OpenApi/test/Microsoft.AspNetCore.OpenApi.Tests/Integration), extended in [PR #60278](https://github.com/dotnet/aspnetcore/pull/60278)).
- **The .NET-native community pattern, established since 2019**: run the API in-memory via `WebApplicationFactory<T>`, `GET /swagger/v1/swagger.json`, and `.ShouldMatchApproved()`. Joseph Woodward: "The approval file is committed to source control, meaning those diffs to the contract are visible to anyone reviewing your changes, whilst also keeping a history of changes to the public contract" ([post](https://josephwoodward.co.uk/2019/08/approval-testing-open-api-swagger-documents)).
- **Bottom line**: `regenerate → git diff --exit-code`, or a snapshot test, is a **DIY convention with zero first-party support, zero documentation and zero roadmap commitment.** The one asymmetry to exploit: `Microsoft.Extensions.ApiDescription.Client` *does* hard-fail the build if a referenced spec is **missing** — a missing spec is caught, a **stale** one is not.

### Client generators

| | NSwag | Kiota | openapi-generator |
|---|---|---|---|
| Stars / open issues | 7,351 / **2,051** | 3,786 / 227 | 26,629 / **5,723** |
| Latest | v14.7.1 2026-04-20 | v1.34.1 2026-07-09 | v7.24.0 2026-07-20 |
| Cadence | maintainer commits thin — the two most recent `master` commits are both dependency bumps; **~3 months with no maintainer commit** | near-daily | ~monthly |
| Runtime needed | .NET | .NET | **Java 11+** |
| Shape | one flat partial client, method per operation | **per-segment fluent request builders** | template-driven |
| Interfaces | `/GenerateClientInterfaces` | **none** — quickstart uses the concrete class | varies |
| Wire layer | `HttpClient` directly, `/InjectHttpClient` | own abstractions + swappable request adapter | varies |
| .NET 9/10 | v14.6.3 notes ".NET 10 libraries and SDK"; targets resolve `$(NSwagDir_Net100)` | yes | `-g csharp`, default library `generichost` |

- **NSwag's funding model, not an abandonment notice**: "developed and maintained by Rico Suter and other contributors", with "Please contact Rico Suter for paid consulting and support" ([README](https://raw.githubusercontent.com/RicoSuter/NSwag/master/README.md)). No "seeking maintainers" notice.
- **Microsoft has de-listed NSwag for new work**: the NSwag tutorial's monikers stop at `aspnetcore-8.0`, noting that in ".NET 9 and later… NSwag isn't included by default" ([tutorial](https://learn.microsoft.com/en-us/aspnet/core/tutorials/getting-started-with-nswag?view=aspnetcore-9.0)).
- **`<OpenApiReference>` has no current Learn page.** The tutorial documents NSwagStudio, the CLI and `NSwag.MSBuild` — not `<OpenApiReference>`. The `dotnet openapi` tool page returns **404**. **The authoritative documentation is the MSBuild source files.**
- **The real NSwag option surface is in the targets file, not the wiki.** The wiki's `CommandLine` page omits `GenerateClientInterfaces`, `InjectHttpClient`, `UseBaseUrl`, `GenerateOptionalParameters`, `GenerateDtoTypes` and `ExceptionClass`. All are present as `%(NSwag*)` item metadata mapping to `openapi2csclient` switches in [`NSwag.ApiDescription.Client.targets`](https://raw.githubusercontent.com/RicoSuter/NSwag/master/src/NSwag.ApiDescription.Client/NSwag.ApiDescription.Client.targets), alongside `NSwagClientBaseClass`, `NSwagDisposeHttpClient`, `NSwagWrapDtoExceptions`, `NSwagHttpClientType`, `NSwagGenerateSyncMethods`, `NSwagContractsNamespace`, `NSwagTypeAccessModifier` and more.
- **Microsoft does NOT position Kiota as NSwag's successor.** Kiota maintainer `baywet`: "One is not meant to replace the other, they just address different segments with different needs"; `darrelmiller`: "Kiota minimizes effort to add feature support across languages at the expense of independence" ([kiota #1709](https://github.com/microsoft/kiota/issues/1709)). AutoRest **is** deprecated — "AutoRest is deprecated and will be retired on July 1, 2026" ([repo](https://github.com/Azure/autorest)) — and its named successor is **TypeSpec**, not Kiota ([autorest #5175](https://github.com/Azure/autorest/issues/5175)).
- Kiota's own framing of its output: "Peeking into the auto-generated code is discouraged" ([experience docs](https://learn.microsoft.com/en-us/openapi/kiota/experience)). Support is a per-language maturity matrix (Stable/Preview/Experimental/Abandoned), not a blanket GA ([support](https://learn.microsoft.com/en-us/openapi/kiota/support)).
- **openapi-generator disclaims its own output**: generators are community contributions, and "We do not guarantee the output by the generator would work appropriately and securely" ([FAQ](https://github.com/OpenAPITools/openapi-generator/wiki/FAQ)). `csharp-netcore` no longer appears in the generator list; **Unverified:** no changelog entry documents the rename/merge.

### ⚠️ `ShortSchemaNames` — it is a FastEndpoints option, not NSwag's, and it makes schema names position-dependent

This is the correction most likely to change a decision, and it is verified at source three ways.

**It is not NSwag's.** A global GitHub code search for `ShortSchemaNames` returns hits in `FastEndpoints/FastEndpoints` and hundreds of consumer `Program.cs` files, and **zero in `RicoSuter/*`** (`gh api search/code?q=ShortSchemaNames+repo:RicoSuter/NSwag` → `total_count: 0`; DeepWiki over both NSwag and NJsonSchema likewise reports no definition).

**What it is.** A `FastEndpoints.Swagger.DocumentOptions` property, doc-commented in source as: "set to true if you'd like schema names to be just the class name instead of the full name" ([DocumentOptions.cs L102-105](https://github.com/FastEndpoints/FastEndpoints/blob/main/Src/Swagger/DocumentOptions.cs#L102-L105)). Public docs confirm: "The full name, including namespace, of DTO classes are used to generate schema names by default" ([openapi-documents](https://fast-endpoints.com/docs/openapi-documents)).

**How it plugs into NSwag.** FastEndpoints replaces NSwag's `ISchemaNameGenerator` wholesale: `settings.SchemaSettings.SchemaNameGenerator = new SchemaNameGenerator(opts.ShortSchemaNames);` ([Extensions.cs L416](https://github.com/FastEndpoints/FastEndpoints/blob/main/Src/Swagger/Extensions.cs#L416)).

**The generator** ([SchemaNameGenerator.cs](https://github.com/FastEndpoints/FastEndpoints/blob/main/Src/Swagger/SchemaNameGenerator.cs)):
- `false` (default): `type.FullName` minus the arity suffix, then `.Replace(".", string.Empty)` → `MyApp.Features.Orders.Response` becomes `MyAppFeaturesOrdersResponse`. **Namespace-derived, therefore globally unique — the collision counter never fires.**
- `true`: substring after the last `.` → the same type becomes just `Response`. **Namespace discarded, so every same-named DTO in the document collides.**
- Generics append `Of…And…` in both modes.

**Why the names become position-dependent.** The short generator returns the bare colliding name and hands collision resolution to NJsonSchema:

1. `JsonSchemaAppender.AppendSchema` passes **the keys already appended** as the reserved set:
   `var typeName = _typeNameGenerator.Generate(schema, typeNameHint, RootSchema.Definitions.Keys);` ([JsonSchemaAppender.cs](https://github.com/RicoSuter/NJsonSchema/blob/master/src/NJsonSchema/JsonSchemaAppender.cs)). **`RootSchema.Definitions.Keys` is whatever has been encountered so far — the reserved set is a function of traversal order.**
2. `DefaultTypeNameGenerator.GenerateAnonymousTypeName` appends the counter ([DefaultTypeNameGenerator.cs](https://raw.githubusercontent.com/RicoSuter/NJsonSchema/master/src/NJsonSchema/DefaultTypeNameGenerator.cs)):
   ```csharp
   var count = 1;
   string typeName;
   do { count++; typeName = typeNameHint + count; }
   while (reservedTypeNames.Contains(typeName));
   ```
   → first `Foo` wins the bare name, next becomes **`Foo2`**, next `Foo3`. If nothing is free it degrades to `Definitions["ref_" + Guid.NewGuid()]`.

**FastEndpoints' newer `Microsoft.OpenApi`-based package reproduces the identical semantics, and its own test concedes the order dependence in its name** — `short_schema_name_collisions_get_deterministic_suffixes_without_prescan`, asserting `Thing` for the type registered first and `Thing2` for the second ([ShortSchemaNameCollisionTests.cs](https://github.com/FastEndpoints/FastEndpoints/blob/main/Tests/IntegrationTests/FastEndpoints.OpenApi/ShortSchemaNameCollisionTests.cs)). The allocator is `SchemaNameRegistry.GetOrAdd`, first-come.

**Consequence for a compile-time contract.** The suffix is assigned by registration order, which follows document traversal (endpoints → operations → properties). Add, rename, reorder or version-filter an endpoint *upstream* of the collision and `Thing`/`Thing2` swap owners. The spec diff reads as a rename; the regenerated client's types **silently rebind to different server-side shapes while still compiling.** Under the default `ShortSchemaNames = false` this failure mode cannot occur — namespace-qualified names are unique and the counter is never reached.

**The collision is undocumented.** The FastEndpoints docs warn about name collisions for *endpoint* names ("if your endpoint class names are not unique, enabling this setting will not be possible") but the Short Schema Names section carries **no collision warning at all** ([docs](https://fast-endpoints.com/docs/openapi-documents)).

### Spec-diff tooling in a dotnet CI

| Tool | How you run it | Breaking ruleset | Exit code | OpenAPI versions |
|---|---|---|---|---|
| **oasdiff** | Go binary, `docker run tufin/oasdiff`, brew, GitHub Action | **212 breaking checks**, per-check severity overrides | `--fail-on ERR\|WARN\|INFO` → 1 | 3.0, 3.1 |
| **Criteo.OpenApi.Comparator** | **`dotnet tool install -g Criteo.OpenApi.Comparator.Cli`** | **50+ rules** with numeric IDs, severity Error/Warning/Info | `--strict` elevates warnings to errors; non-zero | **2.0 – 3.0.x only** |
| openapi-diff | Maven, `docker run openapitools/openapi-diff`, brew | compatible/incompatible | `--fail-on-incompatible` | 3.x |
| pb33f openapi-changes | brew, npm, Docker | config-driven | **Unverified** | **3.0, 3.1, 3.2** |
| Spectral | npm | **none** (linter) | 1 on rule failures | 3.1, 3.0, 2.0 |
| Redocly CLI | npm | **none** | — | — |
| buf | binary, npm, Docker, Action | FILE/PACKAGE/WIRE_JSON/WIRE | **100** | Protobuf only |

**Criteo.OpenApi.Comparator is the only .NET-native breaking-change detector**, and it is genuinely usable: `openapi-compare -o old.json -n new.json -f Json`, `--strict` for "breaking changes are errors instead of warnings", 287.4K downloads, Apache-2.0, library on `netstandard2.0` and CLI on `net6.0`/`net8.0`, built on `Microsoft.OpenApi` ([README](https://raw.githubusercontent.com/criteo/openapi-comparator/master/README.md), rules and severity model verified via DeepWiki over [criteo/openapi-comparator](https://github.com/criteo/openapi-comparator)). Example rules: `RemovedPath` (1005, Error), `TypeChanged` (1026, Error), `RemovedRequiredParameter` (1009, Error), `AddingRequiredParameter` (1010, Warning), `AddedOptionalParameter` (1043, Info).

**🔴 A version trap that disqualifies it for new .NET services.** Criteo's comparator supports **OpenAPI 2.0 through 3.0.x**. But ASP.NET Core's built-in generator defaults to **OpenAPI 3.1 in .NET 10** and **3.2 in .NET 11** ([docs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi?view=aspnetcore-10.0)). A default-configured .NET 10 service emits a document its only .NET-native comparator cannot read. It also pins `Microsoft.OpenApi >= 1.4.5` while that package is now at `3.9.0`. Mitigations: pin `--openapi-version OpenApi3_0`, or use oasdiff (3.0/3.1) or pb33f (3.0/3.1/3.2) instead.

### Is a .NET consumer a tolerant reader? Only for added fields.

| Behaviour | System.Text.Json default | Source |
|---|---|---|
| Unknown JSON properties | **silently skipped** (`JsonUnmappedMemberHandling.Skip` = default; `Disallow` throws; enum added in .NET 8) | [docs](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.serialization.jsonunmappedmemberhandling) |
| Enum wire format | **numeric by default**; `JsonStringEnumConverter` is opt-in | [docs](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.serialization.jsonstringenumconverter) |
| **Unknown enum string value** | **throws `JsonException`** — no built-in graceful degradation; open since 2021 | [dotnet/runtime#57031](https://github.com/dotnet/runtime/issues/57031) |
| Missing `required` / `[JsonRequired]` member | **throws** (.NET 7+) | [docs](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/required-properties) |
| Missing constructor parameters (records) | **silently defaulted** pre-.NET 9; .NET 9 adds opt-in `RespectRequiredConstructorParameters` | same |

**Net for a .NET consumer**: tolerant for *added* fields; **strict** (throws) on a new enum value; **silently wrong** for a removed field on a plain nullable property or a record constructor parameter below .NET 9. **Zalando #108 requires you to tolerate new output enum values; System.Text.Json's default does the opposite.** That is a concrete, testable gap.

### PactNet — the .NET laggard

| Signal | Value |
|---|---|
| Stars / open issues | 929 / 37, MIT, `archived: false`, no deprecation notice |
| Latest release | **5.0.1, 2025-03-22** — ~16 months old |
| Stable releases in ~3 years | **two** (5.0.0 2024-10-06, 5.0.1 2025-03-22) |
| Default-branch HEAD | **2025-11-10**, "chore: fix json schema url" — **~8.7 months, no `master` commit** |
| Commit cadence | ~10 commits across ~18 months, nearly all chores/CI/deps |
| TFMs | **`netstandard2.0` only** — no `net8.0`/`net9.0`/`net10.0` target |
| Spec support | v2/v3/v4; **plugins NOT supported** — "PactNet is currently compliant up to and including Pact Specification Version 4.0, excluding pact plugins" ([README](https://raw.githubusercontent.com/pact-foundation/pact-net/master/README.md)) |
| Plugins | [RFC #492](https://api.github.com/repos/pact-foundation/pact-net/issues/492) open since **2024-02-18**, design deadlock — adamrodger "I really dislike the 'raw' untyped approach"; YOU54F, 2025-09-03: "It requires someone with a bit of time, care & .NET knowledge" |
| gRPC | [PR #548](https://github.com/pact-foundation/pact-net/pull/548) open since 2025-09-04, blocked on #551; Fellows still asking in **2026-04**; a user reports it blocks their work |
| Open crash bug | **#535 "Test Host Process Crashes After 5.0.1 Upgrade"** — open since 2025-03-28, updated 2026-07-29, **unfixed because there has been no release** |
| Maintainer | adamrodger (165 commits); the project has run a co-maintainer search before ([#282](https://github.com/pact-foundation/pact-net/issues/282), 2021) |

- **🔴 A direct collision with the standard ASP.NET Core test seam.** PactNet's README states you cannot use `Microsoft.AspNetCore.Mvc.Testing` to host your API for provider tests — `TestServer`/`WebApplicationFactory` run in-memory, so the Rust FFI core cannot reach the API over a socket. **Provider tests must bind a real TCP port.**
- **The staleness is a binding-layer problem, not a core one.** `pact-reference` (the shared Rust core) was pushed **2026-07-30**; `pact-js` shipped **eight stable releases in 2026** (v17.0.1 2026-07-01 back to v16.1.0 2026-02-06). **pact-net shipped zero in sixteen months.** pact-net had **3 commits by 3 authors in the trailing 12 months**; pact-js had **100+ commits by 8 authors**.
- **The native-core pin quantifies the drift.** pact-net master pins `FFI_VERSION="0.4.27"` ([download-native-libs.sh](https://github.com/pact-foundation/pact-net/blob/master/build/download-native-libs.sh)), released 2025-03-19. The Rust core has since shipped 0.4.28, 0.5.0, 0.5.1, 0.5.2, 0.5.3 and **0.5.4 (2026-04-30)** — **six releases and a minor generation behind, 16.4 months** ([pact-reference releases](https://github.com/pact-foundation/pact-reference/releases)).
- **No open issue asks whether the project is dead** — a GitHub issue search for `maintained OR dead OR abandoned OR deprecated` returned `total_count: 0`. The signal is cadence, not a declaration. The README meanwhile still claims Pact is "the de-facto API contract testing tool" — against the 17% adoption figure in §18.

### Swashbuckle: the "unmaintained" claim is two years stale

- **Verified**: .NET 9 removed Swashbuckle from templates, announced by `JeremyLikness` on 2024-03-18 — "The project is no longer actively maintained by its community owner", "there is not an official release for .NET 8" ([discussion #58103](https://github.com/dotnet/aspnetcore/discussions/58103)). Note this is a GitHub discussion; the .NET 9 breaking-changes index has **no Swashbuckle entry** ([index](https://learn.microsoft.com/en-us/dotnet/core/compatibility/9.0)).
- **Also verified: the justification is now false.** Swashbuckle shipped `v10.2.0` → `v10.2.3` between 2026-05-30 and 2026-06-22, was pushed 2026-07-29, and supports ASP.NET Core ≥ 8.0 with OpenAPI 3.1/3.0/2.0. It was revived by a new core team after the 2024 handover request ([issue #2773](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/issues/2773)).

### Response-validation packages in .NET: the honest answer is "none mature"

- `FluentAssertions.Web` (3.0M) — rich HTTP response assertions, **no OpenAPI validation** ([nuget](https://www.nuget.org/packages/FluentAssertions.Web/)).
- `WireMock.Net.OpenApiParser` (33.9M) — OpenAPI **→ stub**, the opposite direction ([nuget](https://www.nuget.org/packages/WireMock.Net.OpenApiParser/)).
- The .NET-native move is snapshot-gating the document itself (§20, staleness gate), with `PublicApiGenerator` (4.8M) as the established analogue for assembly surface.

---

## 21. Generated client vs anti-corruption layer

### The authoritative statements

- **Microsoft's ACL pattern** describes "a facade or adapter layer between different subsystems that don't share the same semantics", whose purpose is "to ensure that dependencies on outside subsystems don't limit an application's design", crediting Eric Evans ([ACL pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/anti-corruption-layer)).
- **Its stated caveats** matter for the cost side: the layer "adds latency to calls between the two systems"; it "adds an extra service that you must manage and maintain"; you must plan for its scaling, its monitoring/release/configuration integration, and whether it is permanent or retired after migration.
- **Its scoping rule is the answer to "when not to"**: the pattern may be unsuitable when "the new and legacy systems have no significant semantic differences", and in that case "it's important to focus the anti-corruption layer on translation logic. Avoid placing business rules or orchestration in the layer."
- **And its own framing separates the two jobs explicitly**: "the core purpose of an anti-corruption layer is to protect the domain model, not to prescribe any specific product choice." Its Azure reference implementation splits them across two components — **API Management handles protocol concerns; Azure Functions performs "REST order data transfer object (DTO) to domain model, and domain model to legacy DTO"**.
- **Derek Comartin** states the harm concretely: external DTOs "often don't share the same semantics or data structures. Left unchecked this leads to convoluting up your own boundary" ([codeopinion](https://codeopinion.com/anti-corruption-layer-for-mapping-between-boundaries/)).
- **Unverified:** Evans' own DDD Reference definition — [domainlanguage.com's PDF](https://www.domainlanguage.com/wp-content/uploads/2016/05/DDD_Reference_2015-03.pdf) returned **403**. The canonical context-mapping pattern list (Shared Kernel, Customer/Supplier, Conformist, Anticorruption Layer, Separate Ways, Open Host Service, Published Language) is therefore unverified against a primary source.

### Substitute or complement? The evidence says complement, and Microsoft's own reference splits them

The two layers do different jobs:

| | Generated client | Anti-corruption layer |
|---|---|---|
| Translates | wire format → CLR types | provider's model → **your** model |
| Authored by | a generator, from the spec | you, by hand |
| Breaks on | a spec change it cannot type | a semantic change you chose to absorb |
| Detects | structural mismatch **against the committed spec** | nothing — it *absorbs* |
| Fails when | the spec is stale or the name binding shifts (§20) | the provider's semantics change silently |

- **A generated client cannot substitute for an ACL** because it is *definitionally conformist*: it reproduces the provider's model in your process. That is the Conformist pattern, not an anti-corruption layer — the generator has no knowledge of your domain to translate into.
- **An ACL cannot substitute for a generated client** because it does not detect anything. Its job is to absorb change, which is the *opposite* of raising a signal. Every semantic change it successfully absorbs is a change you never hear about.
- **Microsoft's reference implementation demonstrates the split** rather than arguing it: protocol/facade in API Management, DTO↔domain mapping in Functions, with observability for translation failures ([ACL pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/anti-corruption-layer)).
- **The tension is real and stated by Microsoft, not invented here**: if there are no significant semantic differences, the ACL is discouraged and should hold translation only. So a consumer whose model genuinely matches the provider's is being told by Microsoft that the mapping layer is not warranted — and by Comartin that skipping it lets external semantics leak. Both positions are available from these sources.
- **Unverified:** no practitioner post arguing the opposite case ("mapping DTOs is boilerplate; use the generated types directly") was reached in a primary source. The counter-position should be treated as unrepresented in this document rather than as absent from the world.

---

## 22. Why async contracts got a registry model and sync HTTP did not

The evidence points at **structure**, not accident — but with a significant reframing at the end.

**1. Fan-out and unknown consumers.** Hugo Guerrero (Red Hat): "Kafka's asynchronous communication does not allow the producer to know who will consume the data, or when", and out-of-band coordination fails because "New clients added later will likely miss that information" ([Red Hat Developers](https://developers.redhat.com/blog/2021/05/04/event-driven-apis-and-schema-governance-for-apache-kafka-get-ready-for-kafka-summit-europe-2021)). Gwen Shapira states the requirement as temporally unbounded: readers must understand data written by upstream writers "at all times" ([Confluent](https://www.confluent.io/blog/schema-registry-kafka-stream-processing-yes-virginia-you-really-need-one/)).

**2. Replay of persisted events — and this is Confluent's *stated* reason for its default.** BACKWARD is the default because it "allows you to rewind consumers to the beginning of the topic" ([schema evolution docs](https://docs.confluent.io/platform/current/schema-registry/fundamentals/schema-evolution.html)). Shapira on the cost of getting it wrong: changes "can require very painful re-processing of stored historical data". Yan Cui: events "will outlive the code that produced them", and "Since events are never deleted, we need to have a way to be able to replay (read) old events" ([theburningmonk](https://theburningmonk.com/2025/04/event-versioning-strategies-for-event-driven-architectures/)).

**3. The requirement predates Kafka by five years, which undercuts the accidental-history reading.** Pat Helland's *Data on the Outside vs Data on the Inside* (CIDR 2005) derives it from the nature of data crossing a service boundary in a message: "The contents of a message are always from the past! They are never 'now.'", and prescribes that all message schemas be versioned with each message carrying a version-dependent identifier. Cited via [Adrian Colyer's summary](https://blog.acolyer.org/2016/09/13/data-on-the-outside-versus-data-on-the-inside/); **Unverified:** the ACM reprint 403'd and the [CIDR PDF](https://www.cidrdb.org/cidr2005/papers/P12.pdf) returned unparsed binary, so all Helland quotes are secondary-sourced.

**4. The sharpest mechanical asymmetry: Avro ships a reader/writer schema-resolution algorithm; JSON-over-HTTP ships nothing.** The Avro spec: "the original schema must be provided along with the data. However, the reader may be programmed to read data into a different schema", with formal resolution rules — a reader-only field takes its default, a writer-only field is ignored, and `int`→`long`/`float`/`double` promotions are specified ([Avro 1.12.0 spec](https://avro.apache.org/docs/1.12.0/specification/)). Kleppmann: schema evolution "allows you to update different components of the system independently, at different times, without worrying about compatibility" ([post](https://martin.kleppmann.com/2012/12/05/schema-evolution-in-avro-protocol-buffers-thrift.html)).
- **Kleppmann himself locates the asymmetry in the economics of shipping the schema**: in RPC contexts "it's probably too much overhead to send the schema with every request and response." A persisted message amortises a schema id over an immutable record; a request/response pair does not.
- **The consequence is decisive.** Confluent's BACKWARD/FORWARD modes are *checkable* because they are defined against a resolution algorithm that already exists in a spec. JSON-over-HTTP has no reader/writer resolution model at all, so "is this change backward compatible?" has **no algorithmic referent** — which is why every OpenAPI tool must invent its own taxonomy. Compare oasdiff's hand-curated **212 breaking rules** against Avro's compact promotion table.

**5. Surface area — one direction versus two, in one artifact.** Eric Wittmann's Apicurio blocker (§5) is the best-sourced articulation: the same JSON Schema in `components` serves as request input and response output, and "Compatibility rules will be different for inputs vs. outputs" ([discussion #1696](https://github.com/Apicurio/apicurio-registry/discussions/1696)). A Kafka message has one direction. Corroborating scale: oasdiff needs 506 rules across 7 surfaces (paths, params, body, responses, headers, schema, security).

**6. Negotiation.** A synchronous caller can be told 410 Gone, can be versioned, can content-negotiate; a consumer replaying a log cannot negotiate with the past. **Unverified:** no source was found joining content negotiation to the missing-registry question. Treat as inference.

**7. The reframing that changes the question.** Apicurio rates **compatibility = Full for Avro, Protobuf, JSON Schema and XSD — and = None for AsyncAPI**, GraphQL, Kafka Connect and WSDL ([rule reference](https://www.apicur.io/registry/docs/apicurio-registry/3.1.x/getting-started/assembly-rule-reference.html)). AsyncAPI's own breaking-change tooling is `@asyncapi/diff` at **28 stars** ([repo](https://github.com/asyncapi/diff)), and the spec org has an **open issue titled "What do we define as a breaking change?"** ([asyncapi/spec#688](https://github.com/asyncapi/spec/issues/688)).
- **So the async *description language* has no enforced compatibility either — only the async *serialization formats* do.** The registry model tracks **schema languages that carry a resolution model**, not async-ness. Protobuf-over-HTTP landing on the enforced side (§10) and AsyncAPI landing on the unenforced side both confirm it.
- **The question is therefore not "why async and not sync"** but "why *schema languages with a resolution algorithm* and not *description languages without one*". On that framing, an HTTP API described by Protobuf gets the registry; an event API described by AsyncAPI does not.

**8. The accidental-history counter-reading.** **Unverified:** no source was found arguing the asymmetry is accidental (e.g. Confluent's commercial interest in Avro versus REST's organic growth). What the available evidence suggests against it: Helland derived the requirement in 2005, pre-Kafka; and Apicurio — a Red Hat project with no Avro-serialization stake in OpenAPI — tried and failed for reasons its maintainer named. The one datum *for* the accidental reading is that Optic, the best-funded OpenAPI breaking-change product, is archived — consistent with insufficient demand rather than impossibility.

---

## 23. "Do nothing more" as a legitimate answer — and the counter-argument

### The case, at full strength
§16 states it. Its three strongest legs:
1. **YAGNI**, with four named costs ([Yagni](https://martinfowler.com/bliki/Yagni.html)).
2. **The test pyramid** puts cross-service checks at the brittle, expensive top ([TestPyramid](https://martinfowler.com/bliki/TestPyramid.html)).
3. **Monorepo atomicity** — atomic change and large-scale refactoring are documented monorepo benefits ([CACM 2016](https://research.google/pubs/why-google-stores-billions-of-lines-of-code-in-a-single-repository/)).

### The counter-argument, which is the crux of the whole document

**Independent *deployability*, not repo layout, creates the need.**

- **Juho Snellman states it precisely**: "Obviously you can't do an atomic change in that case, since you need to continue supporting the old server implementation until all client binaries have been upgraded." His conclusion: deployment constraints, not monorepo benefits, determine whether atomic cross-project change is feasible — and expand/migrate/contract remains necessary regardless of repo layout ([A monorepo misconception](https://www.snellman.net/blog/archive/2021-07-21-monorepo-atomic/)).
- **Malte Ubl names the phenomenon**: version skew occurs when two systems with a dependency between them deploy non-atomically and temporarily run at different versions. His mitigations are forward *and* backward compatible APIs, version locking, bounded rollback windows and explicit path versioning ([Version Skew](https://www.industrialempathy.com/posts/version-skew/)).
- **The monorepo advocates themselves refuse the lockstep assumption.** Nx's myth #6: packages in monorepos can have independent versions and release cycles, and lockstep versioning is a choice, not a monorepo requirement ([10 Monorepo Myths Debunked](https://nx.dev/blog/monorepo-myths-debunked)). **The "do nothing" argument silently assumes the property its own advocates say is optional.**
- **Someone measured the blast radius. Uber**, across 500,000 commits in their Go monorepo: **1.4% of commits impacted more than 100 services and 0.3% impacted over 1,000.** Their response was not to trust the monorepo — they built deploy orchestration where one service's deployment decision consults signals from other impacted services, with 0–5 tiering and staged rollouts ([InfoQ](https://www.infoq.com/news/2025/09/uber-monorepo-deployment/)). **A monorepo at scale produced more cross-service machinery, not less.**
- **Zalando makes the same distinction in different vocabulary**: an *incompatible* change becomes a *breaking* change only once deployed against a live consumer ([#106](https://opensource.zalando.com/restful-api-guidelines/#106)). Compiler and shared integration suite catch the incompatibility at change-time; **neither catches it at deploy-time, and the deploy window is where the skew lives.**
- **Fowler's axis, restated**: a published interface is one whose callers you cannot reach with a refactoring tool ([PublishedInterface](https://martinfowler.com/bliki/PublishedInterface.html)). A monorepo makes callers *reachable*; it does not make them *already redeployed*.
- **The ADOPT-ring restraint pattern points at the tooling.** ThoughtWorks rates API expand-contract ADOPT and names its prerequisite as consumer visibility, "perhaps through a technique such as consumer-driven contract testing" ([entry](https://www.thoughtworks.com/en-us/radar/techniques/api-expand-contract)). Restraint and detection are not alternatives in that framing — the first requires the second.
- **Fowler's own tolerant-reader recommendation ends in the same place**: share your reader code and tests with the provider so their build detects the break ([TolerantReader](https://martinfowler.com/bliki/TolerantReader.html)).

### What survives of the restraint case
- The distinction is **change-time versus deploy-time**, not repo-layout. A monorepo with a single CI genuinely does collapse change-time detection to "the build fails" — for structural breaks, in the consumer code that exists.
- It does **not** cover: deploy-window skew; in-flight requests during rolling deploys; semantic and behavioural breaks; or the case where the provider deploys ahead of the consumer.
- **Unverified:** no source with standing publishes "monorepo ⇒ skip contract testing", and no credible numeric threshold exists. The defensible statement is qualitative: detection earns its keep when you cannot reach all callers at change-time **and** cannot deploy them atomically.

---

## 24. Negative findings — looked for, not found

These are decision-relevant. An absence of evidence here is evidence about the state of the practice.

1. **No first-party .NET staleness gate.** Verified at code level: no `--verify`/`--check` option on `dotnet-getdocument`, no MSBuild property, no docs mention of comparing or CI, and no roadmap item. `git diff --exit-code` is a DIY convention (§20).
2. **No standardised definition of "breaking".** The OAI declined the scope ([#3793](https://github.com/OAI/OpenAPI-Specification/discussions/3793)); every ruleset is vendor-invented and configurable (§1).
3. **No consumer-aware OpenAPI diffing exists.** Not in oasdiff, not in Optic. Only Pact/PactFlow (via published consumer contracts) and Apollo/Hive (via traffic telemetry) compute an affected-consumer set (§7, §9).
4. **No established OpenTelemetry-based contract-break detection.** Three search framings produced no primary-authority writeup. Listing it as an option means inventing it (§13).
5. **No named engineer's first-person "we ripped out Pact" post.** The strongest removal evidence in the whole corpus is second-hand and comes from **Pact's own founder** ([Skurrie](https://pactflow.io/blog/a-disastrous-tale-of-ui-testing-with-pact/)). The named critique that does exist is analytical (Risi) or competitor-authored (Specmatic, Speakeasy).
6. **No "we deleted contract tests and kept e2e" post** either. Both directions lack a first-person account.
7. **No numeric threshold** for when contract testing starts paying. Anyone quoting one is inventing it (§16).
8. **No Microsoft statement positioning Kiota as NSwag's successor** — its maintainers say the opposite, and TypeSpec is AutoRest's named successor (§20).
9. **No prior-year baseline for Postman's 17% contract-testing figure.** The 2022/2023/2024 reports publish none, and the sample collapsed ~7× in between, so **the 17% cannot be read as a decline or a rise** (§18). No SmartBear, Kong, Nordic APIs or Gartner figure was reached; Stack Overflow, JetBrains and CNCF ask nothing on the topic at all.
10. **No radar blip has ever been moved to Hold in this space.** Pact, Pactflow, Spectral, CDC-as-technique and API expand-contract were all simply **dropped**. Sixteen named tools — including oasdiff, Buf, Optic, Specmatic, Microcks, Schemathesis, NSwag and Kiota — **never blipped at all** across Vols 20–34 (§18).
11. **No source arguing the async/sync registry asymmetry is accidental history** (§22).
12. **No primary source for Evans' ACL definition** — the DDD Reference PDF 403'd, so the canonical context-mapping pattern list is unverified here (§21).
13. **No practitioner post arguing generated types should flow straight into the domain.** The counter-position to §21 is unrepresented here, not disproven.
14. **No explicit Buf statement disclaiming OpenAPI support.** Established by absence across docs and blog (§10).
15. **No executable breaking-change rules in the two published corporate Spectral/Redocly rulesets checked.** adidas' 275-line ruleset contains zero matches for `breaking|compatib|deprecat|version`; Otto's compatibility rules are prose only (§17).
16. **Documentation-vs-reality conflicts left unresolved rather than resolved**: Apicurio's matrix says OpenAPI compatibility Full, its maintainer says unimplemented (§5); Redocly markets breaking-change detection its OSS CLI does not have (§7); oasdiff's 506/212 rule counts appear only on the vendor site, not in-repo (§7); PactNet's README calls Pact "the de-facto API contract testing tool" against a 17% survey figure (§20).

### Method caveats affecting this document
- **WebSearch budget was exhausted (200/200)** partway through. Later research is WebFetch-only, which is why the survey items and radar movement are the weakest sections.
- **Fetched pages are rendered through a summarizing model.** One fetch of the ThoughtWorks Pact blip URL returned an entirely different blip's content, and another invented a volume number; both were discarded. §18's ring/volume data was consequently re-derived from `pdftotext` over 15 official volume PDFs rather than from blip pages, and `/radar/<quadrant>/<slug>` was found to **silently 404 into the current volume listing** — a guard against that is required by anyone re-running this. Any quotation intended for external use should be spot-checked against its URL.
- **One research thread failed.** The agent assigned to the anti-corruption-layer and shared-DTO-package questions terminated on an API error. §21 and §11 were assembled from direct fetches instead, which is why Evans' primary text and the counter-position to §21 are marked unverified.
- **Paywalled/blocked sources**: ACM (cacm, dl.acm, queue — 403), domainlanguage.com DDD Reference (403), the Medium/Stackademic anti-Pact post (unreachable through two redirect hops), `docs.pactflow.io` bi-directional pages (404), `useoptic.com` (socket errors), `redmonk.com` progressive-delivery origin post (404).
- **Competitor-authored sources are labelled inline.** Specmatic's headline claim that CDC blocks parallel development is **not substantiated by its own article body**; Speakeasy's and Signadot's critiques of Pact are commercially interested.

---

## 25. Summary matrix

| Family | Catches | Misses | Machinery | .NET maturity | Evidence of use |
|---|---|---|---|---|---|
| **CDC (Pact)** | structural + enumerative **per known consumer**; deploy-order verdict | side effects; unrecorded interactions; semantics | Broker, provider states, both pipelines, 7-step ladder | **Weak** — PactNet: 16 mo no release, `netstandard2.0` only, no plugins/gRPC, FFI 6 releases behind, incompatible with `WebApplicationFactory` | **Radar: technique Adopt ×3 (2015–16) then retired; tool peaked Trial Jan 2015, absent 8 volumes.** Postman 2025: **17%**. `can-i-deploy` in **204** public workflows. 18.9M NuGet but top version is the deprecated 4.5.0; **zero use in Microsoft's own eShop reference apps** |
| **Bi-directional (PactFlow)** | consumer-use ∩ provider-spec, decoupled pipelines | that the provider implements its spec | PactFlow (**paid**), spec publish + consumer publish | via PactNet + OpenAPI | Vendor-only; no independent adoption data |
| **Provider-driven publishing** | provider-verified spec conformance | which consumers exist / what they use | spec publish + generated provider tests | **None** (JVM) | **Spring Cloud Contract archived 2026-07**; successor at 5 stars |
| **Schema registry (Confluent)** | structural, per compatibility mode, **with upgrade order** | semantics; integrity constraints | registry server + CI check | n/a for HTTP | Default BACKWARD; industry standard for Kafka |
| **HTTP registry analogue** | — | — | — | — | **Does not exist.** Apicurio's OpenAPI rule unimplemented per its maintainer |
| **Runtime request/response validation** | spec-vs-implementation drift | consumer impact; version-to-version breaks | proxy or middleware + live traffic | Prism/Redocly `drift` (Node); Kusk dead | Criteo measured ~5% invalid calls |
| **Spec-diff in CI** | structural, enumerative, some protocol — **212 rules (oasdiff)** | semantics; whether the spec matches the code; **which consumer cares** | spec artifact + baseline + CI step | **oasdiff** (Docker/binary) or **Criteo.Comparator** (`dotnet tool`, **3.0.x max**) | oasdiff 4.2M Docker pulls, **394** public workflows; **never blipped on the radar**; **Optic archived Jan 2026**; Yelp's own tool admits it cannot cover all cases |
| **Linting (Spectral)** | rules that *prevent* breaks | any two-version comparison — **structurally** | npm + ruleset | via npm | **1.53M weekly npm, 926 workflows** — most-installed, detects nothing. Radar Trial Oct 2022, then dropped. adidas' published ruleset has **zero** compatibility rules |
| **Generated client** | structural mismatch **vs the committed spec**, at compile time | enum additions, nullability, status codes, semantics, side effects, whether the provider serves that spec | committed spec + build-time codegen + a staleness gate | **Strong tooling** (NSwag 40.9M / Kiota 134.6M), **but no first-party staleness gate** | Ubiquitous; `ShortSchemaNames` makes names position-dependent (§20) |
| **Shared DTO NuGet** | compile-time structural, when the consumer upgrades | everything until they upgrade; **production skew entirely** | package publish + SemVer | trivial | Common; no authority endorses it across a BC boundary |
| **IDL-first (Buf)** | wire/JSON/source compatibility, **formally decidable**; **BSR rejects the push** | REST semantics, status codes, headers, URL shape | `.proto` + `buf` + optional BSR | `Grpc.Tools` 345M; buf runs via npm/Docker/Action | **`buf breaking` in 1,332 public workflows — the most-wired breaking-change gate found, and it has never appeared on the radar.** 11.3k stars, 555k downloads per release |
| **GraphQL schema checks** | breaking **iff observed traffic uses it**; usage-% thresholds | non-GraphQL APIs | router telemetry + registry | n/a | Apollo mature; Hive console only 483 stars |
| **Spec-driven test generation** | spec-vs-implementation conformance, incl. backward-compat (Specmatic) | business logic; consumer identity | JVM/Python/Docker + a running service | **Specmatic 0 NuGet; Schemathesis via Docker/Action; Microcks .NET module 27K downloads**; **Dredd archived** | Schemathesis 3.5k stars but **unfunded, $433 lifetime** |
| **Traffic shadowing / response diff** | **semantic + behavioural** breaks nothing else sees | requires production traffic; post-change | shadow env, mutation isolation, noise cancellation | none .NET-specific | Diffy 1.5k stars; **Mixpanel and Netflix each built their own**; Netflix caught encoding and float-precision drift no schema diff can see |
| **Usage telemetry on the provider** | the **actual blast radius** of a planned removal | the break itself — it sizes, it does not block | annotate call sites + a metrics warehouse + report tooling | DIY | **Shopify's `mark_breaking`**; Apollo/Hive do the managed version for GraphQL |
| **Runtime monitoring** | real breaks, in production | prevention — **strictly post-breach** | eBPF/agent or gateway + telemetry | — | Postman Insights **still beta after 3 yrs**, agent 16 stars; APIClarity archived |
| **E2E across both services** | integration + side effects + persistence | speed, determinism, scale | both services deployed together; shared env | standard (`WebApplicationFactory`, Testcontainers) | **Two ThoughtWorks HOLD rings** (shared env HOLD in 2017 *and again* 2024); Pact itself keeps a smoke subset. Contrast: **Testcontainers reached Adopt** |
| **Versioning discipline** | *prevents* rather than detects | anything the discipline is not followed for | guidelines + review + deprecation headers | `Asp.Versioning.Http` 137M | Zalando/AIP/Azure/Stripe/GitHub/Shopify all do this; **Azure's gate is a human review board**; but only **26%** use SemVer (Postman 2025), and Zalando's *linter* stalled while its *rules* kept evolving |
| **Tolerant reader** | absorbs additive change | turns a *removal* into a silent wrong answer; **STJ throws on new enum values** | client discipline | STJ tolerant on fields, **strict on enums** | **RFC 9413 rebuts naive tolerance**; Zalando rejects unknown *input*; "Tolerant reader" **never blipped** on the radar |
| **Do nothing more** | change-time structural breaks, in a single CI | deploy-window skew; in-flight requests; semantics; provider-ahead-of-consumer | none | free | **Nobody with standing publishes the monorepo exemption**; Uber built *more* machinery, not less. But ThoughtWorks' CDC blip says the technique "requires no special tool", and **none of Stripe/Shopify/Netflix/Uber adopted contract testing** |
