# flagd: what it is, what it isn't, and how to land it here

Research note (2026-08-09). Primary sources only — the flagd documentation and its source tree, `open-feature/flagd-schemas`, `open-feature/dotnet-sdk-contrib`, `open-feature/dotnet-sdk`, `open-feature/open-feature-operator`, `open-telemetry/opentelemetry-demo`, the OpenTelemetry semantic-conventions registry, nuget.org, the GitHub REST API, and the GHCR OCI registry. No listicles, no blog paraphrases.

Every port, environment variable name, default value, TFM, licence, version and publish date below was read on the surface cited, on 2026-08-09. Where a rendered doc page and the source disagree, both are reported and the source wins — see *Negative findings*. Container facts (entrypoint, uid, base layers) were read out of the image config blob on ghcr.io, not inferred from a Dockerfile.

**This note does not re-argue the provider choice.** [`feature-flag-providers.md`](feature-flag-providers.md) settled that: stay on OpenFeature, put flagd behind it. This note answers *how*, for the nine flag-reading processes in this repo, and it corrects three things that note and [ADR-0014](../adr/0014-feature-flags-openfeature.md) get wrong or leave open.

**Two corrections to carry forward before reading further.** OpenFeature is CNCF **Incubating**, not graduated, and ADR-0014's `.AddFileProvider(...)` / `.AddLaunchDarklyProvider(...)` samples are not real APIs. Neither is repeated below; both are already recorded in [`feature-flag-providers.md` § 6](feature-flag-providers.md#6-negative-findings) and are listed here only as ADR amendments (§ 11).

---

## 1. The UI question, answered plainly

**flagd has no UI, and the project says so itself.** From flagd's own introduction: "It doesn't include a UI, management console or a persistence layer. It's configurable entirely via a POSIX-style CLI." ([docs/index.md](https://github.com/open-feature/flagd/blob/main/docs/index.md)). The CLI has exactly two subcommands, `start` and `version` ([flagd CLI reference](https://github.com/open-feature/flagd/blob/main/docs/reference/flagd-cli/flagd.md)) — there is no `flagd set`, no admin API, no write path of any kind. flagd reads flag definitions; nothing in the daemon writes them.

**Your day-to-day flag flip is: edit a JSON file in Git, and flagd picks it up within ~1 second with no restart.** Not a dashboard, not a toggle, not an approval. That is the whole workflow, and § 7 documents the mechanism and its measured latency. For this repo that is arguably correct — the flag file is diffable, reviewable and reproducible, which is what a reference solution wants. For a team that needs a non-engineer to kill a feature at 03:00 it is disqualifying, and no amount of tooling below changes that.

### 1.1 `flagd-ui` — it exists, but it is not a flagd component

There is **no `open-feature/flagd-ui` repository.** Enumerating all 60 repos in the `open-feature` GitHub organisation (`GET /orgs/open-feature/repos`) returns no UI project: the closest things are [`open-feature/cli`](https://github.com/open-feature/cli) (code generation, § 1.5), [`open-feature/mcp`](https://github.com/open-feature/mcp), and demo apps (`playground`, `toggle-shop`, `cloud-native-demo`, `killercoda`).

The `flagd-ui` people mean is **a service inside the OpenTelemetry Astronomy Shop demo**: [`open-telemetry/opentelemetry-demo/src/flagd-ui`](https://github.com/open-telemetry/opentelemetry-demo/tree/main/src/flagd-ui). Facts, from that tree:

| | |
|---|---|
| **Owner** | `open-telemetry`, not `open-feature`. Apache-2.0 ([repo metadata](https://api.github.com/repos/open-telemetry/opentelemetry-demo)) |
| **Stack** | A [Phoenix](https://www.phoenixframework.org/) (Elixir) app ([README](https://github.com/open-telemetry/opentelemetry-demo/blob/main/src/flagd-ui/README.md)) |
| **Shipped as a container?** | Yes — `ghcr.io/open-telemetry/demo:latest-flagd-ui`, verified pullable (manifest HTTP 200 on ghcr.io, 2026-08-09). It is built as `${IMAGE_NAME}:${DEMO_VERSION}-flagd-ui` from [`compose.yaml`](https://github.com/open-telemetry/opentelemetry-demo/blob/main/compose.yaml), with `IMAGE_NAME=ghcr.io/open-telemetry/demo` in [`.env`](https://github.com/open-telemetry/opentelemetry-demo/blob/main/.env) |
| **Port** | 4000 (`FLAGD_UI_PORT=4000`) |
| **Can it edit flags?** | **Yes, and it writes back to the sync source.** It mounts `./src/flagd:/app/data` — the same directory flagd mounts at `/etc/flagd` — so a save rewrites the file flagd is watching, and flagd's file sync reloads it |
| **Programmatic surface** | `GET /feature/api/read`, `POST /feature/api/write` (whole-document replace: "*all* the data will be rewritten by this write operation"), plus legacy `/read-file`, `/write-to-file` ([README](https://github.com/open-telemetry/opentelemetry-demo/blob/main/src/flagd-ui/README.md)) |
| **Maturity** | It is demo scaffolding. No independent release stream, no versioning of its own, no auth (the compose entry hard-codes a `SECRET_KEY_BASE` in plain text), and its stated purpose is "configuring the feature flags of the flagd service" **for the demo** |

**Verdict for this repo: no.** It is an Elixir service whose write model is "replace the entire flag document", with no authentication, versioned only alongside a demo you do not run. Adopting it means giving up the Git-as-governance property that is the only governance flagd has, in exchange for a form. If the owner wants a laptop UI to *look at*, it works and it is one compose service; if the owner wants a flag-management surface, it is not one.

### 1.2 The OpenFeature Kubernetes Operator

[`open-feature/open-feature-operator`](https://github.com/open-feature/open-feature-operator) — 309 stars, actively pushed (2026-07-31), latest release **v0.9.2 (2026-05-27)**. Its CRDs, read from [`config/crd/bases`](https://github.com/open-feature/open-feature-operator/tree/main/config/crd/bases):

- `FeatureFlag` — the flag definition itself, as a Kubernetes object (`spec.flagSpec.flags`, the same schema as § 4).
- `FeatureFlagSource` — where a workload's flags come from.
- `Flagd` — a managed flagd deployment.
- `InProcessConfiguration` — in-process resolver settings for injected sidecars.
- Two deprecated predecessors are still present: `FeatureFlagConfiguration`, `FlagSourceConfiguration`.

**The management surface it provides is `kubectl` and RBAC, not a UI.** That is a real answer to governance — `kubectl auth can-i patch featureflags`, admission control, GitOps via Argo/Flux, an audit trail in the API server — but it is a Kubernetes answer. **This repo runs docker-compose; the operator is out of scope entirely.** The one adjacent UI is third-party: [`jabenedicic/headlamp-plugin-openfeature`](https://github.com/jabenedicic/headlamp-plugin-openfeature) (Apache-2.0, 3 stars, `v0.2.0` 2026-07-20), a Headlamp plugin that views and edits those four CRDs.

### 1.3 `flagd-proxy` — what it is and is not

[`flagd-proxy`](https://github.com/open-feature/flagd/tree/main/flagd-proxy) lives in the flagd repo and releases on its own cadence (`flagd-proxy/v0.9.7`, 2026-07-27). Its README carries an **`experimental` stability badge**.

It **is** a Kubernetes-specific fan-out: "a pub sub for deployed flagd sidecar containers to subscribe to change events in FeatureFlag CRs" — it watches `FeatureFlag` custom resources and re-exposes them over the flagd gRPC sync protocol, so in-process providers get CR changes without each one holding a Kubernetes watch.

It **is not** a UI, an admin API, a write path, or a general-purpose flag server. It has no relevance to a docker-compose topology.

### 1.4 Third-party editors, and whether any vendor speaks flagd

Searching the GitHub repository index for `flagd ui` surfaces four candidates. None is close to production:

| Repo | Licence | Stars | Releases | Last push |
|---|---|---|---|---|
| [`yzx396/flagd-ui`](https://github.com/yzx396/flagd-ui) — "Minimal UI to generate flagd definition" | MIT | 10 | none | 2025-12-29 |
| [`justinabrahms/flagd-ui`](https://github.com/justinabrahms/flagd-ui) — "Management UI for flagd" | Apache-2.0 | 5 | v0.2.2 (2026-02-19) | 2026-02-19 |
| [`onova-tech/flagd-admin`](https://github.com/onova-tech/flagd-admin) — "Admin UI and API to manage flagd flag definitions" | Apache-2.0 | 1 | none | 2026-02-07 |
| [`randiapr/flarus`](https://github.com/randiapr/flarus) — Deno + Rust + SurrealDB | — | 0 | none | 2025-09-17 |

Single-maintainer, single-digit-star projects holding the write path to your production flag file. Do not.

**No hosted vendor speaks flagd's sync protocol.** Surfaces searched: the `open-feature` org repo list, the [flagd sync-configuration reference](https://flagd.dev/reference/sync-configuration/) (which enumerates every supported sync source and lists no vendor), the [gRPC sync service reference](https://flagd.dev/reference/grpc-sync-service/), the [buf schema registry entry](https://buf.build/open-feature/flagd) for `flagd.sync.v1`, and two web searches for commercial `flagd.sync.v1` implementations. The only named implementations of the interface are flagd itself and flagd-proxy. Portability away from flagd runs through **OFREP** (§ 9), not through the sync protocol.

### 1.5 The one first-party tool worth knowing

[`open-feature/cli`](https://github.com/open-feature/cli) generates strongly typed flag accessors from a *flag manifest* — including C# (`openfeature generate csharp --namespace ...`). It would replace this repo's hand-written `CatalogFeatureFlags` / `BffFeatureFlags` / `CheckoutSagaFeatureFlags` constant classes. Two caveats that keep it out of the migration plan: the repo README is badged **WIP** with "expect breaking changes", and the C# generator's own doc says **"Stability: alpha"** ([`openfeature_generate_csharp.md`](https://github.com/open-feature/cli/blob/main/docs/commands/openfeature_generate_csharp.md)). Its manifest is also a separate schema from the flagd flag definition, so it is a second file to keep in sync, not a replacement for `flags.json`.

---

## 2. Architecture and resolver modes

flagd splits into "the evaluation engine runs in a separate process" and "the evaluation engine runs inside your app" ([architecture](https://flagd.dev/architecture/)). The .NET provider exposes three `ResolverType` values — `RPC`, `IN_PROCESS`, `FILE` ([`FlagdConfig.cs`](https://github.com/open-feature/dotnet-sdk-contrib/blob/main/src/OpenFeature.Providers.Flagd/FlagdConfig.cs)).

| | **RPC** | **IN_PROCESS** | **FILE** |
|---|---|---|---|
| Where evaluation happens | In the flagd daemon (Go) | In your process (.NET), against rules pulled over gRPC | In your process (.NET), against a local file |
| Wire protocol | `flagd.evaluation.v2` over gRPC/Connect + an event stream | `flagd.sync.v1` `SyncFlags` stream | none |
| Default port | 8013 | 8015 | n/a |
| Per-evaluation latency | one network round trip; flagd "typically takes <10ms for an evaluation" ([architecture](https://flagd.dev/architecture/)) | in-memory | in-memory |
| Caching | LRU cache of `reason=STATIC` results only — "Evaluations for flags with targeting rules are never cached" ([provider spec](https://flagd.dev/reference/specifications/providers/)) | n/a (whole ruleset is local) | n/a |
| Daemon unreachable | stream drops → `PROVIDER_STALE`, serve `STALE` from cache where possible; past the grace period → `PROVIDER_ERROR`, **cache purged**, every evaluation falls to the caller's `defaultValue` | serves `STALE` from the last-known ruleset indefinitely | unaffected — no daemon involved |
| Ruleset exposure | rules never leave the daemon | full ruleset lives in every process | full ruleset lives in every process |

Reconnection is specified, not incidental: the gRPC retry policy retries `UNAVAILABLE`/`UNKNOWN` four times at 1s/2s/4s, and *on top of that* the provider applies an application-level backoff from `retryBackoffMs` (1000) doubling to `retryBackoffMaxMs` (12000) before re-establishing a stream, explicitly to stop tight loops when an L7 proxy returns stream errors immediately ([provider spec § Stream Reconnection](https://flagd.dev/reference/specifications/providers/)).

### 2.1 Recommendation for the 9-process topology

**RPC in compose and in any deployed environment; FILE for tests and for `dotnet run` without the stack.** In-process is the credible alternative and is rejected on specific grounds, not on taste.

Why RPC wins here:

1. **One evaluator, therefore one answer.** A checkout crosses BFF → Catalog → Ordering → Inventory → Payments → saga. Under RPC all nine processes ask the same Go engine holding one merged store; a flag file edit flips the store atomically for everyone. Under in-process or file, nine independent .NET evaluators each converge on their own schedule (§ 7), so two services can legitimately disagree for the duration of a poll interval or a sync round trip. For `checkout.payment-then-stock`, which changes step *order*, a mid-checkout disagreement is a correctness bug, not a UX wrinkle.
2. **It removes the cross-implementation hashing question entirely.** The daemon hashes with [`twmb/murmur3`](https://github.com/open-feature/flagd/blob/main/core/go.mod) and evaluates JsonLogic with `diegoholiveira/jsonlogic/v3`; the .NET provider hashes with the `murmurhash` NuGet package and evaluates with `JsonLogic` 6.0.2 (json-everything), pinned exactly ([csproj](https://github.com/open-feature/dotnet-sdk-contrib/blob/main/src/OpenFeature.Providers.Flagd/OpenFeature.Providers.Flagd.csproj)). Two engines, two hash implementations. flagd has an **accepted ADR** whose entire premise is that this divergence is real (§ 10.1). With RPC there is exactly one engine and the question does not arise.
3. **The failure mode is the one the code already handles.** Every call site in this repo passes an explicit `defaultValue`, and the OpenFeature spec guarantees the default on abnormal execution. Daemon down → `catalog.show-discontinued-in-search` returns `false`, `bff.home-page-eager-cache-warm` returns `true`, `checkout.payment-then-stock` returns `false` — all three the safe branch, all three already tested.
4. **Teaching value.** flagd emits its own traces and metrics for every evaluation (§ 9). Under RPC the flag evaluation is a visible span in Jaeger with the daemon on the other end; under in-process it is invisible.

Why in-process is a defensible different call: the saga and the Kafka consumers evaluate outside any HTTP request, and under RPC a daemon outage past the grace period flips them to code defaults, whereas in-process would keep serving the last-known rules. If the owner weights "never lose the configured value" above "never disagree between services", in-process is the right answer and the whole of § 6 still applies unchanged — only the provider env vars move to `FLAGD_RESOLVER=in-process` / port 8015. It is a one-line change per service, so this is a reversible decision, not a fork in the road.

**FILE is not a third contender — it is the test and laptop path**, and it is the reason the migration can be done and proven without a container (§ 8).

---

## 3. Sync sources

These configure the **daemon**, not the .NET provider. Two syntaxes exist: the `--uri` shorthand where the prefix implies the provider, and `--sources` where the `provider` field says it explicitly and extra options become available ([sync configuration](https://flagd.dev/reference/sync-configuration/)).

| Provider | `--uri` prefix | Example | Notes |
|---|---|---|---|
| `file` | `file:` | `file:etc/flagd/my-flags.json` | Only `.yaml`/`.yml`/`.json` extensions are accepted ([`flagd start`](https://github.com/open-feature/flagd/blob/main/docs/reference/flagd-cli/flagd_start.md)) |
| `fsnotify` / `fileinfo` | — (`--sources` only) | `{"uri":"flags.json","provider":"fileinfo"}` | Explicit choice of watch mechanism; see § 7 |
| `http` / `https` | `http(s)://` | `https://my-flags.com/flags` | Polls at `interval` (default **5s**, max 86400). Honours `ETag`/`If-None-Match`. `authHeader`, `headers`, and an `oauth` block (`clientID`/`clientSecret`/`tokenURL`, or a `folder` of secret files with `ReloadDelayS`) |
| `grpc` / `grpcs` | `grpc(s)://` | `grpc://my-flags-server` | Streaming. `tls`, `certPath`, `providerID`, `selector`, `maxMsgSize` (default 4 MB), experimental `incrementalUpdates` |
| gRPC custom resolvers | `envoy://`, `dns://`, `uds://`, `xds://` | `envoy://localhost:9211/test.service` | |
| `kubernetes` | `core.openfeature.dev/` | `core.openfeature.dev/default/my-crd` | Watches a `FeatureFlag` CR |
| `gcs` | `gs://` | `gs://my-bucket/my-flags.json` | Polls; application default credentials |
| `azblob` | `azblob://` | `azblob://my-container/my-flags.json` | Polls; `AZURE_STORAGE_ACCOUNT` + env credentials |
| `s3` | `s3://` | `s3://my-bucket/my-flags.json` | Polls; standard AWS credential chain |

Multiple sources merge into one store, **last-defined wins** on duplicate keys, and a delete triggers a full resync from every source so a shadowed definition reappears rather than 404-ing ([syncs](https://flagd.dev/concepts/syncs/)).

**For this repo: `file:` in docker-compose** — the flag file is version-controlled, which is the property ADR-0014 wanted, and it needs no credentials or extra infrastructure. **For a deployed environment: `http` against a static object store, or `s3`/`gcs`/`azblob` directly** — CI publishes the reviewed file, flagd polls it with an ETag, and `intervalSeed` (set per instance) keeps N replicas from stampeding. Do not reach for `grpc` unless something upstream actually streams `flagd.sync.v1`; nothing in this repo does, and nothing you can buy does either (§ 1.4).

---

## 4. The flag definition schema, and the rewritten `flags.json`

**The schemas live in [`open-feature/flagd-schemas`](https://github.com/open-feature/flagd-schemas), not `open-feature/schemas`** — `json/flags.json` and `json/targeting.json`, currently at version **0.2.15** ([`json/version.txt`](https://github.com/open-feature/flagd-schemas/blob/main/json/version.txt), Apache-2.0). They are also embedded as resources in the .NET provider assembly, which validates flag documents against them at load time.

### 4.1 Document shape

```json
{
  "$schema": "https://flagd.dev/schema/v0/flags.json",
  "flags": { },
  "$evaluators": { },
  "metadata": { }
}
```

| Field | Required | Meaning |
|---|---|---|
| `flags` | **yes** (`required: ["flags"]` in the schema) | Map of flag key → flag object. The daemon additionally accepts an array form with a `key` property per entry; providers accept only the map |
| `$schema` | the [reference doc](https://flagd.dev/reference/flag-definitions/) says required; the JSON schema does not list it | Editor tooling only |
| `$evaluators` | no | Named shared targeting rules, referenced as `{"$ref": "myRule"}`. **Nested `$ref` is not supported** — each shared evaluator must be self-contained ([provider spec](https://flagd.dev/reference/specifications/providers/)) |
| `metadata` | no | Flag-set metadata. `flagSetId` and `version` are named in the schema; arbitrary string/number/boolean keys are allowed |

Per flag (`baseFlag` in the schema — `required: ["state", "variants"]`):

| Field | Required | Meaning |
|---|---|---|
| `state` | **yes** | `"ENABLED"` \| `"DISABLED"`. Disabled flags "are treated as if they don't exist" |
| `variants` | **yes** | Object, ≥1 entry, **all values the same type** (boolean / number / string / object) |
| `defaultVariant` | no | Variant name, or `null`. Omitted/null means flagd returns no `value` or `variant` at all and the SDK applies the **code-defined default** ([troubleshooting](https://flagd.dev/troubleshooting/)) |
| `targeting` | no | A JsonLogic rule returning a variant name, `true`/`false` (coerced to the `"true"`/`"false"` variant keys), or `null` to fall through to `defaultVariant` |
| `metadata` | no | Flag-level; wins over flag-set metadata on key collision |

flagd injects three context properties automatically: `targetingKey` (the OpenFeature targeting key, lifted to a top-level property), `$flagd.flagKey`, and `$flagd.timestamp` (Unix seconds). Context also merges from `-H` header mappings and `-X` static values at startup, in that priority order over the request body.

### 4.2 Targeting is JsonLogic, plus four custom operators

`targeting.json` is a closed schema (`additionalProperties: false` at every level), so the accepted operator set is exactly what it enumerates: `var`, `missing`, `missing_some`, `if`, `==`/`===`/`!=`/`!==`, `>`/`>=`/`<`/`<=` (the two `<` forms accept a third operand for between-tests), `%`, `/`, `*`, `+`, `-`, `min`, `max`, `merge`, `cat`, `substr`, `map`/`filter`/`reduce`/`all`/`none`/`some`, `in`, `!`, `!!`, `and`, `or`, plus flagd's own:

- **`starts_with` / `ends_with`** — `{"starts_with": [{"var":"email"}, "admin"]}`; exactly two string-or-rule operands.
- **`sem_ver`** — `{"sem_ver": [{"var":"version"}, "^", "1.2.0"]}`; exactly three operands, middle one from `= != > < >= <= ~ ^` (`~` matches minor, `^` matches major).
- **`fractional`** — § 4.3.
- **`$ref`** — resolves a named entry from `$evaluators` in place; the provider substitutes it before evaluating.

### 4.3 `fractional`, in detail

This is the operator ADR-0014's headline "gradual rollout" needs, and the one the owner has been burned by before.

- **Hash.** "This works by hashing ([murmur3](https://github.com/aappleby/smhasher/blob/master/src/MurmurHash3.cpp)) the given data point and using an algorithm leveraging pure integer arithmetic, with `math.MaxInt32` (2,147,483,647) as the maximum weight sum." ([fractional operation](https://github.com/open-feature/flagd/blob/main/docs/reference/custom-operations/fractional-operation.md))
- **Sticky? Yes, explicitly.** "Assignment is deterministic (sticky) based on the expression supplied as the first parameter." The same page demonstrates it: "Notice that rerunning either curl command will always return the same variant and value. The only way to get a different value is to change the email or update the `fractional` configuration." **This is the concrete contrast with `Microsoft.Percentage`**, which draws a fresh `RandomGenerator.NextDouble()` per evaluation. flagd's rollout is a cohort; `Microsoft.Percentage` is a coin flip per request.
- **Bucketing key default.** If the first element is omitted, "a concatenation of the `targetingKey` and the `flagKey` will be used" — the schema words the same fact as "Defaults to a concatenation of the flagKey and targetingKey". The recommended *explicit* form is `{"cat": [{"var":"$flagd.flagKey"}, {"var":"<your key>"}]}`, and the reason is stated: seeding with the flag key "so that experiments running across different flags are statistically independent".
- **The bucketing value must be a string** today. Other primitives must be cast with `cat`.
- **Weights need not sum to 100.** They are *relative* integer weights; omitted weight defaults to `1`; the total must not exceed 2,147,483,647. A note on the page records that "Previous versions of the `fractional` operation used percentage-based weights that had to sum to 100 and were limited to 1% precision" — that restriction is gone, and precision now goes to ~0.00000005%. Writing `["on", 10], ["off", 90]` still yields 10% because the weights are relative and happen to total 100; that is the readable form and it is what § 4.4 uses.
- **Keying on a specific context property**: put it in the `cat`, e.g. `{"cat": [{"var":"$flagd.flagKey"}, {"var":"buyerId"}]}`. Anything stable for the subject works — `targetingKey`, a session id, an email.

### 4.4 The rewrite of this repo's `flags.json`

The current file uses a schema that is nobody's: `{"targeting": {"if": {"op": "percentage", "attribute": "targetingKey", "value": 10}, "then": "on"}}`. `op`/`percentage`/`attribute`/`then` do not exist in flagd's targeting schema — and, separately, `JsonFlagLoader` never reads the block at all (§ 11.1). Both problems die with this file.

```json
{
  "$schema": "https://flagd.dev/schema/v0/flags.json",
  "flags": {
    "catalog.show-discontinued-in-search": {
      "state": "ENABLED",
      "variants": { "on": true, "off": false },
      "defaultVariant": "off",
      "targeting": {
        "if": [
          { "var": "targetingKey" },
          {
            "fractional": [
              { "cat": [{ "var": "$flagd.flagKey" }, { "var": "targetingKey" }] },
              ["on", 10],
              ["off", 90]
            ]
          },
          null
        ]
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
  },
  "metadata": {
    "flagSetId": "dotnetatlas",
    "version": "1"
  }
}
```

Four decisions in that file worth stating, because they are not obvious:

1. **The `if` guard around `fractional` is load-bearing, not decoration.** Without a `targetingKey` in the context, `{"var":"targetingKey"}` resolves to null and the `cat` degenerates to the flag key alone — identical for every evaluation, so the *entire service* buckets as one subject and the flag reads 100% on or 100% off. The guard makes a missing targeting key return `null`, which the targeting schema defines as: "a null value 'exits' the targeting, and the `defaultValue` is returned, with the reason indicating the targeting did not match." Missing key ⇒ `off`. That is the correct conservative behaviour, and it means § 11's slice ordering (targeting key *before* fractional) is a hard dependency, not a preference.
2. **The two boolean flags carry no `targeting` block** — they are a kill switch and a topology switch, not rollouts. They will resolve with `reason=STATIC`, which is the only reason the RPC provider is allowed to cache (§ 2). The rollout flag has targeting and is therefore never cached, which is correct: a cached cohort assignment would be indistinguishable from a stale one.
3. **Flag-set `metadata` populates telemetry for free.** `flagSetId` and `version` are the two keys the OpenFeature .NET SDK reads out of flag metadata into `feature_flag.set.id` and `feature_flag.version` (§ 9). Bumping `version` on every edit is what makes "did these two services evaluate against the same ruleset?" answerable in Jaeger instead of a guess.
4. **`state: "ENABLED"` and `variants` are mandatory; `defaultVariant` is not.** Setting `defaultVariant: null` would delegate to the `defaultValue` at each C# call site. Keeping an explicit `defaultVariant` is better here because the file then documents intent on its own, and the C# defaults become a second line of defence rather than the only statement of what "off" means.

---

## 5. The .NET provider

[`OpenFeature.Providers.Flagd`](https://www.nuget.org/packages/OpenFeature.Providers.Flagd) — **0.7.2, published 2026-06-30, Apache-2.0**. Versions on the flat container index: `0.0.2, 0.6.0, 0.6.1, 0.7.0, 0.7.1, 0.7.2`; 0.7.2 is the newest, and the un-prefixed ID supersedes the deprecated `OpenFeature.Contrib.Providers.Flagd` (last released `v0.5.0`, 2026-04-01).

- **Repo:** yes, it lives in [`open-feature/dotnet-sdk-contrib`](https://github.com/open-feature/dotnet-sdk-contrib) under `src/OpenFeature.Providers.Flagd`. Repo licence Apache-2.0.
- **TFMs, read from the csproj** (not from the NuGet page): `netstandard2.0; net462; net8.0; net9.0; net10.0`. `net10.0` is present, which matters — this repo pins .NET 10.
- **OpenFeature dependency:** `<OpenFeatureVersion>[2.9,2.99999]</OpenFeatureVersion>` applied to both `OpenFeature` and `OpenFeature.Hosting`. **2.14.0 is inside that range**, so the provider does not block the bump; it requires it be ≥2.9.0.
- **Repo health.** Five releases in four months (0.6.0 → 0.7.2, 2026-04-10 → 2026-06-30) and substantive commits *after* the last release: `feat(flagd): add retry backoff configuration options` (2026-07-28), `fix(flagd): Add support for FLAGD_SYNC_PORT` (2026-07-21), `feat(flagd): add FILE resolver type` (2026-06-24), `fix(flagd): apply in-process flag updates after initial sync` (2026-06-16). Twelve open flagd-tagged issues. This is a maintained package, not a shim — a meaningful contrast with the dormant providers catalogued in [`feature-flag-providers.md` § 3.1](feature-flag-providers.md#31-net-sdk-and-openfeature-status).
- **Transitive dependencies** (net10.0): `Google.Protobuf 3.30.2`, `Grpc.Net.Client 2.71.0`, `JsonLogic [6.0.2]` (exact pin), `NJsonSchema 11.0.0`, `Semver 3.0.0`, `murmurhash 1.0.3`.

### 5.1 Configuration surface

Every environment variable name below is a `const` in [`FlagdConfig.cs`](https://github.com/open-feature/dotnet-sdk-contrib/blob/main/src/OpenFeature.Providers.Flagd/FlagdConfig.cs); every default is the field initialiser in [`FlagdProviderOptions.cs`](https://github.com/open-feature/dotnet-sdk-contrib/blob/main/src/OpenFeature.Providers.Flagd/DependencyInjection/FlagdProviderOptions.cs). Constructor/builder values take precedence over environment variables.

| Env var | `FlagdProviderOptions` | `FlagdConfigBuilder` | Default | Applies to |
|---|---|---|---|---|
| `FLAGD_HOST` | `Host` | `WithHost(string)` | `localhost` | rpc, in-process |
| `FLAGD_PORT` | `Port` | `WithPort(int)` | `8013` | rpc |
| `FLAGD_SYNC_PORT` | — (via `Port`) | `WithPort(int)` | `8015` when resolver is in-process; takes priority over `FLAGD_PORT` | in-process |
| `FLAGD_TLS` | `UseTls` | `WithTls(bool)` | `false` | rpc, in-process |
| `FLAGD_SERVER_CERT_PATH` | `CertificatePath` | `WithCertificatePath(string)` | `""` | rpc, in-process |
| `FLAGD_SOCKET_PATH` | `SocketPath` | `WithSocketPath(string)` | `""` | rpc, in-process. **If set, `HOST`/`PORT`/`TLS`/`CERT_PATH` are all ignored** |
| `FLAGD_CACHE` | `CacheEnabled` | `WithCache(bool)` | **`false`** (env value `lru` enables) | rpc |
| `FLAGD_MAX_CACHE_SIZE` | `MaxCacheSize` | `WithMaxCacheSize(int)` | **`10`** | rpc |
| `FLAGD_MAX_EVENT_STREAM_RETRIES` | `MaxEventStreamRetries` | `WithMaxEventStreamRetries(int)` | `3` (`0` ⇒ `int.MaxValue`) | rpc, in-process |
| `FLAGD_RESOLVER` | `ResolverType` | `WithResolverType(ResolverType)` | `RPC`; values `rpc`, `in-process`, `file` | all |
| `FLAGD_SOURCE_SELECTOR` | `SourceSelector` | `WithSourceSelector(string)` | `""` | rpc, in-process |
| `FLAGD_OFFLINE_FLAG_SOURCE_PATH` | `OfflineFlagSourcePath` | `WithOfflineFlagSourcePath(string)` | `""` | file |
| `FLAGD_HASH_FILE_CHANGE` | `UseHashFileChangeDetection` | `WithUseHashFileChangeDetection(bool)` | `false` | file |
| `FLAGD_OFFLINE_POLL_MS` | `OfflinePollIntervalMs` | `WithOfflinePollIntervalMs(int)` | `5000` | file |
| `FLAGD_DEADLINE_MS` | `DeadlineMs` | `WithDeadlineMs(int)` | `300000` | file (init timeout) |
| `FLAGD_RETRY_BACKOFF_MS` | — | `WithRetryBackoffMs(int)` | `1000` | rpc, in-process |
| `FLAGD_RETRY_BACKOFF_MAX_MS` | — | `WithRetryBackoffMaxMs(int)` | `12000` | rpc, in-process |
| n/a | — | `WithLogger(ILogger)` | `NullLogger` | all |

Five options in the cross-language [flagd provider specification](https://flagd.dev/reference/specifications/providers/) have **no .NET implementation**: `FLAGD_TARGET_URI`, `FLAGD_STREAM_DEADLINE_MS`, `FLAGD_RETRY_GRACE_PERIOD`, `FLAGD_KEEP_ALIVE_TIME_MS`, `FLAGD_PROVIDER_ID`. Three .NET defaults also contradict the spec's stated defaults — see *Negative findings*.

### 5.2 DI registration, per mode

The DI extensions live in namespace `OpenFeature.DependencyInjection.Providers.Flagd` ([`FeatureBuilderExtensions.cs`](https://github.com/open-feature/dotnet-sdk-contrib/blob/main/src/OpenFeature.Providers.Flagd/DependencyInjection/FeatureBuilderExtensions.cs)) with four overloads: `AddFlagdProvider()`, `AddFlagdProvider(Action<FlagdProviderOptions>)`, `AddFlagdProvider(string domain)`, `AddFlagdProvider(string domain, Action<FlagdProviderOptions>)`. Options are read through `IOptionsMonitor<FlagdProviderOptions>` keyed by `FlagdProviderOptions.DefaultName` (`"FlagdProvider"`) or by the domain — which is exactly the seam tests need (§ 8.3).

```csharp
using OpenFeature;
using OpenFeature.DependencyInjection.Providers.Flagd;
using OpenFeature.Hooks;
using OpenFeature.Providers.Flagd;

// RPC — talk to the flagd daemon (compose / deployed).
services.AddOpenFeature(builder =>
{
    builder.AddFlagdProvider(o =>
    {
        o.ResolverType = ResolverType.RPC;
        o.Host = "flagd";
        o.Port = 8013;
        o.CacheEnabled = true;   // caches reason=STATIC only; harmless for targeted flags
        o.MaxCacheSize = 100;    // the 10 default is a footgun as the flag set grows
    });
    builder.AddHook(new TraceEnricherHook());
    builder.AddHook(new MetricsHook());
});

// IN_PROCESS — pull the ruleset over the sync stream, evaluate locally.
builder.AddFlagdProvider(o =>
{
    o.ResolverType = ResolverType.IN_PROCESS;
    o.Host = "flagd";
    o.Port = 8015;               // sync port, not 8013
    o.SourceSelector = "flagSetId=dotnetatlas";
});

// FILE — no daemon at all (tests, `dotnet run` without the stack).
builder.AddFlagdProvider(o =>
{
    o.ResolverType = ResolverType.FILE;
    o.OfflineFlagSourcePath = flagFilePath;   // relative paths resolve against the process CWD
    o.OfflinePollIntervalMs = 1000;
});
```

Without DI, the provider is constructed directly — `new FlagdProvider()` (env vars), `new FlagdProvider(new Uri("http://localhost:8013"))`, or `new FlagdProvider(config)` from a `FlagdConfigBuilder` — and handed to `Api.Instance.SetProviderAsync(...)`.

---

## 6. The docker-compose service block

Facts the block is built from, each verified:

- **Image:** `ghcr.io/open-feature/flagd`. Latest release `flagd/v0.16.1`, published **2026-07-27**. Tag `v0.16.1` exists on GHCR with digest `sha256:9525b3c2916183810f93f0a72774c1dfad48d1ae22852c753719c46db80af5e7` (read from the `Docker-Content-Digest` header on the manifest request).
- **Ports** ([`flagd start`](https://github.com/open-feature/flagd/blob/main/docs/reference/flagd-cli/flagd_start.md)): `-p/--port` **8013** (flag evaluation, gRPC *and* HTTP/1.1 via Connect); `-m/--management-port` **8014** (`/healthz`, `/readyz`, `/metrics`, and a standard gRPC health check); `-g/--sync-port` **8015** (`flagd.sync.v1`); `-r/--ofrep-port` **8016** (OFREP). All four are unused on this repo's host — the allocated set is 1025, 3000, 4317, 4318, 5341, 5433, 5540, 6379, 6380, 8025, 8080, 8081, 8089–8096, 8100–8108, 8889, 9000, 9011, 9090, 9094, 10000, 16686 — so they map 1:1 with no renumbering.
- **The image is distroless and has no shell.** Its config blob reports `User: 65532:65532`, `Entrypoint: ["/flagd-build"]`, and a history of bazel-built Debian trixie base layers with no package manager, no busybox and no curl. **A Docker `HEALTHCHECK` is therefore impossible for the stock image** — `CMD-SHELL` has no shell, and `["CMD","wget",…]` (the pattern this repo's `x-readiness-healthcheck` anchor uses) has no wget. Options are: omit the healthcheck and gate dependents on `service_started`; or bake a wrapper image the way the BC services already pin an Alpine `-extra` base. **Recommend omitting it** — the OpenFeature default-value guarantee means a not-yet-ready flagd degrades to the code default rather than breaking a service, so the ordering guarantee buys nothing worth an extra image.
- **Move the flag file to `src/flagd/flags.json`.** This repo already keeps per-component infra config in `src/keycloak`, `src/postgres`, `src/grafana`, `src/prometheus`, `src/otel-collector`, `src/nginx-cdn`; a repo-root `flags.json` fits none of them, and bind-mounting a *single file* on Windows breaks the moment an editor saves via write-temp-then-rename (the mount follows the old inode). Mount the directory.
- **Profiles:** `core` **and** `full`, matching `postgresdb`, `redis-basket` and `mailpit`. A developer running `--profile core` plus `dotnet run` (ports 5100–5108) needs flag evaluation just as much as the containerised stack does. Cost: one more 128 MB container in `core`.

```yaml
  # flagd — OpenFeature flag-evaluation daemon (ADR-0014). Reads src/flagd/flags.json and
  # serves all 9 flag-reading processes over the RPC evaluation API on 8013. Flag edits are
  # picked up without a restart: outside Kubernetes flagd's file sync polls os.Stat every 1s
  # (core/pkg/sync/builder/syncbuilder.go), so bind-mount inotify quirks don't apply.
  # No healthcheck: the image is distroless (uid 65532, entrypoint /flagd-build) with no shell
  # and no wget, so the x-readiness-healthcheck anchor can't run in it. Dependents use
  # service_started — a flagd that isn't up yet degrades to the call site's defaultValue,
  # which every call site already passes.
  flagd:
    image: ghcr.io/open-feature/flagd:v0.16.1@sha256:9525b3c2916183810f93f0a72774c1dfad48d1ae22852c753719c46db80af5e7
    container_name: flagd
    restart: unless-stopped
    profiles:
      - core
      - full
    command:
      - "start"
      - "--uri"
      - "file:/etc/flagd/flags.json"
      - "--metrics-exporter"
      - "otel"
      - "--otel-collector-uri"
      - "otel-collector:4317"
    environment:
      - OTEL_SERVICE_NAME=flagd
      # Go runtime soft memory limit, held under the container limit below so GC pressure
      # surfaces before the OOM killer does.
      - GOMEMLIMIT=100MiB
    volumes:
      - ./src/flagd:/etc/flagd:ro
    ports:
      - "8013:8013"   # flag evaluation (gRPC + HTTP/1.1 via Connect)
      - "8014:8014"   # management: /healthz, /readyz, /metrics
      - "8015:8015"   # flagd.sync.v1 stream (in-process resolver)
      - "8016:8016"   # OFREP
    deploy:
      resources:
        limits:
          memory: 128M
          cpus: '0.5'
        reservations:
          memory: 64M
    logging:
      driver: json-file
      options:
        max-size: "10m"
        max-file: "3"
```

Each flag-reading service then gains three environment variables and a `depends_on` entry:

```yaml
    environment:
      - FLAGD_RESOLVER=rpc
      - FLAGD_HOST=flagd
      - FLAGD_PORT=8013
    depends_on:
      flagd:
        condition: service_started
```

Smoke check for the gate (`--metrics-exporter otel` does not disable the readiness probe):

```bash
curl -sf http://localhost:8014/readyz
curl -sX POST 'http://localhost:8016/ofrep/v1/evaluate/flags/bff.home-page-eager-cache-warm'
```

`/readyz` returns HTTP 412 until every sync provider has completed at least one successful sync, then 200 permanently ([monitoring](https://flagd.dev/reference/monitoring/)) — so it is a real signal for "the flag file parsed", which is the failure this repo most wants caught.

---

## 7. Hot reload

**The daemon reloads without a restart, and on Windows it does so by polling, not by inotify.** This is the question the owner's Rancher Desktop environment makes sharp, and the answer is in the source rather than the docs.

[`SyncBuilder.newFile`](https://github.com/open-feature/flagd/blob/main/core/pkg/sync/builder/syncbuilder.go) selects the watch mechanism by looking at one environment variable:

```go
func (sb *SyncBuilder) newFile(uri string, logger *logger.Logger) *file.Sync {
	switch os.Getenv("KUBERNETES_SERVICE_HOST") {
	case "":
		// no k8s service host env; use fileinfo
		return sb.newFileInfo(uri, logger)
	default:
		// default to fsnotify
		return sb.newFsNotify(uri, logger)
	}
}
```

In docker-compose `KUBERNETES_SERVICE_HOST` is unset, so flagd uses the **`fileinfo` watcher — `os.Stat` polling**, and [`NewFileInfoWatcher`](https://github.com/open-feature/flagd/blob/main/core/pkg/sync/file/fileinfo_watcher.go) starts its timer at `1 * time.Second`. **The classic bind-mount inotify failure does not apply**, because inotify is not used. The reload latency is therefore ≤1s plus parse time, consistent with flagd's claim that flag definitions "are monitored for changes which will be immediately reflected in flagd's evaluations" ([docs/index.md](https://github.com/open-feature/flagd/blob/main/docs/index.md)).

Three gotchas that do apply:

1. **Do not set `"provider":"fsnotify"` explicitly** in a `--sources` config on Windows/Docker Desktop. That opts back into inotify over a bind mount, which is the failure mode being avoided. `--uri file:` and `"provider":"file"` both route through the switch above.
2. **A save produces several events, not one.** "Most editors will cause a few filesystem events on a save… Generally speaking, updating a symbolic link will result in only a single event, and may even be atomic" ([troubleshooting](https://flagd.dev/troubleshooting/)). Harmless on a laptop — flagd re-reads and re-merges — but the docs recommend a symlinked watch target for production.
3. **Only `.yaml` / `.yml` / `.json` extensions are accepted** by the file sync.

**On the .NET side (FILE resolver) the same design choice was made independently, and documented.** The provider's watcher "polls the file's modification time and size at a regular interval. Modification-time polling is used by default because native file system event APIs are unreliable in the environments this resolver typically targets (e.g. Linux overlay/NFS mounts and bind-mounted ConfigMaps), where events are frequently missed" ([provider README](https://github.com/open-feature/dotnet-sdk-contrib/blob/main/src/OpenFeature.Providers.Flagd/README.md)). Default poll is `FLAGD_OFFLINE_POLL_MS=5000`; `FLAGD_HASH_FILE_CHANGE=true` switches to MurmurHash content comparison for filesystems whose mtimes lie, at the cost of a full file read per poll. For tests, drop the interval to 1000 ms or lower.

---

## 8. Testing

The repo's bar is: run real what is cheap and deterministic, fake only unmanaged dependencies, assert observable outcomes, and survive a plausible mutation. flagd fits that bar better than the current arrangement does, because the current arrangement fakes the *evaluator* and therefore cannot test a targeting rule at all.

### 8.1 Is there a Testcontainers module for flagd? No.

Surfaces searched, all on 2026-08-09:

- [`testcontainers/testcontainers-dotnet` `src/`](https://github.com/testcontainers/testcontainers-dotnet/tree/main/src) — 66 module directories enumerated via the GitHub contents API. No flagd, no OpenFeature.
- [`testcontainers/testcontainers-java` `modules/`](https://github.com/testcontainers/testcontainers-java/tree/main/modules) and [`testcontainers/testcontainers-go` `modules/`](https://github.com/testcontainers/testcontainers-go/tree/main/modules) — 63 and 86 modules respectively. Neither has flagd (both have `openfga`, which is authorization, not feature flags).
- nuget.org search API for `flagd` — **7 total hits**, none a Testcontainers module: `OpenFeature.Providers.Flagd`, `OpenFeature.Contrib.Providers.Flagd` (deprecated), `CommunityToolkit.Aspire.Hosting.Flagd`, plus four unrelated packages. A search for `Testcontainers flagd` returns **0 hits**.

**It is a plain `ContainerBuilder`, and the canonical example is in the provider's own repo** — [`FlagdTestBedContainer.cs`](https://github.com/open-feature/dotnet-sdk-contrib/blob/main/test/OpenFeature.Providers.Flagd.E2e.Common/FlagdTestBedContainer.cs) builds `ghcr.io/open-feature/flagd-testbed:v{version}`, binds 8013/8014/8015/8016/8080, maps a `./flags` directory with `WithResourceMapping`, and waits on a custom `IWaitUntil` that polls `http://{host}:{8014}/healthz`. [`open-feature/flagd-testbed`](https://github.com/open-feature/flagd-testbed) is the shared cross-SDK harness; its README explicitly recommends "to utilize testcontainers for easier test setup". For this repo the stock `ghcr.io/open-feature/flagd` image is the right one — the testbed adds a launchpad REST control plane the repo does not need.

### 8.2 Yes, integration tests can run with no container at all

`ResolverType.FILE` reads a JSON file and evaluates in-process with the real JsonLogic engine, including `fractional`, `sem_ver` and `starts_with`/`ends_with`. No gRPC stream is created. That is the mode almost every test in this repo should use: it exercises the *actual* rules against the *actual* evaluator, costs a file read, and is deterministic.

### 8.3 Pinning a variant deterministically — including under `fractional`

Three techniques, in order of preference:

1. **Per-test flag file with no targeting.** Write a temp file whose flag has only `variants` + `defaultVariant`. Resolves `STATIC`. Use this whenever the test is about the *consumer* of the flag, not the rule.
2. **Per-test flag file with a 100/0 `fractional` split.** `["on", 100], ["off", 0]` is deterministic by construction — any bucketing value lands in the only non-empty bucket. This is how you prove a call site honours a *targeted* result (`reason=TARGETING_MATCH`) without reasoning about a hash. Invert to `["on", 0], ["off", 100]` for the negative case. A mutation that drops the targeting evaluation flips both.
3. **A hard-coded targeting key discovered empirically against the real 10/90 rule — do not do this.** It looks like the strongest test and is the most fragile: flagd has an *accepted* ADR (§ 10.1) that changes the hash input encoding to CBOR and states "all hashes will change, which will result in rebucketing", explicitly "**for 100% of the users**". Every empirically-pinned key becomes wrong on the release that lands it, and the failure will look like a flaky test rather than a dependency change. If cohort *distribution* must be asserted, assert the statistical property (N keys, ~10% on, within tolerance) rather than a specific key's bucket — and accept that as a slow test, not a per-slice one.

### 8.4 `InMemoryProvider` + isolated API vs the flagd FILE resolver

**Correction to [`feature-flag-providers.md` § 5](feature-flag-providers.md#5-what-this-repo-would-actually-do):** the API is **`OpenFeatureFactory.CreateIsolated()`** in namespace `OpenFeature.Isolated`, not `Api.CreateIsolated()` ([`OpenFeatureFactory.cs`](https://github.com/open-feature/dotnet-sdk/blob/main/src/OpenFeature/Isolated/OpenFeatureFactory.cs)). It is decorated `[Experimental("OFISO001")]`, so calling it directly requires suppressing that diagnostic.

**And it is largely moot, because 2.14.0 does it for you.** `AddOpenFeature` now registers `OpenFeatureFactory.CreateIsolated()` as the singleton `Api` instead of the global `Api.Instance` ([`OpenFeatureServiceCollectionExtensions.cs`](https://github.com/open-feature/dotnet-sdk/blob/main/src/OpenFeature.Hosting/OpenFeatureServiceCollectionExtensions.cs), shipped by [PR #760](https://github.com/open-feature/dotnet-sdk/pull/760), released in [2.14.0 on 2026-06-22](https://github.com/open-feature/dotnet-sdk/blob/main/CHANGELOG.md)). Every `WebApplicationFactory` host gets its own isolated API automatically. **The exact hazard the existing test comments cite — "OpenFeature's `Api.Instance` provider is process-global, so several fixtures sharing it would contaminate each other's flag reads" ([`BffTestHostExtensions.cs`](../../test/EShop.BFF.IntegrationTests/Common/BffTestHostExtensions.cs)) — is fixed by a version bump, not by a test-design change.** That makes the 2.13.0 → 2.14.0 bump slice 0 of the migration.

When each is right:

- **`InMemoryProvider`** — for a test whose subject is a *consumer* and whose flag is a boolean input. `Flag<T>` takes an optional `Func<EvaluationContext, string> contextEvaluator` ([`Flag.cs`](https://github.com/open-feature/dotnet-sdk/blob/main/src/OpenFeature/Providers/Memory/Flag.cs)), so it can even fake targeting — but that delegate is *your* logic, not flagd's, so it proves nothing about the rule that will actually run in production.
- **flagd `ResolverType.FILE`** — for anything whose subject is the *rule*. This is the whole reason to migrate: the rule in the test file and the rule in `src/flagd/flags.json` are read by the same evaluator.
- **`Substitute.For<IFeatureClient>()`** — retire it. It asserts against the substitute, not against the flag; `SearchProductsTests` currently asserts `FeatureClient.Received().GetBooleanValueAsync(...)`, which is a mock-interaction assertion and survives any mutation to the rule.

### 8.5 Worked examples

Unit test, FILE resolver, no container, deterministic split:

```csharp
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFeature;
using OpenFeature.DependencyInjection.Providers.Flagd;
using OpenFeature.Hosting;
using OpenFeature.Model;
using OpenFeature.Providers.Flagd;

namespace Platform.ServiceDefaults.UnitTests.FeatureFlags;

public sealed class FlagdFileResolverTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("flagd-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task TargetedBuyer_ResolvesOn_WhenRolloutIsFullyAllocated()
    {
        // Arrange — 100/0 weights make the bucket deterministic without reasoning about murmur3.
        var path = WriteFlags(onWeight: 100, offWeight: 0);
        await using var provider = BuildHost(path);
        var lifecycle = provider.GetRequiredService<IFeatureLifecycleManager>();
        await lifecycle.EnsureInitializedAsync(TestContext.Current.CancellationToken);

        var client = provider.GetRequiredService<IFeatureClient>();
        var context = EvaluationContext.Builder().SetTargetingKey("buyer-7f3c").Build();

        // Act
        var details = await client.GetBooleanDetailsAsync(
            "catalog.show-discontinued-in-search",
            defaultValue: false,
            context,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert — the value AND the reason: a rule that silently stopped matching would still
        // return true via defaultVariant if the weights were inverted, so pin both.
        details.Value.Should().BeTrue();
        details.Reason.Should().Be("TARGETING_MATCH");
        details.Variant.Should().Be("on");
    }

    [Fact]
    public async Task MissingTargetingKey_FallsBackToDefaultVariant()
    {
        // Arrange — same 100/0 rule; the only difference is an absent targeting key.
        var path = WriteFlags(onWeight: 100, offWeight: 0);
        await using var provider = BuildHost(path);
        await provider.GetRequiredService<IFeatureLifecycleManager>()
            .EnsureInitializedAsync(TestContext.Current.CancellationToken);

        var client = provider.GetRequiredService<IFeatureClient>();

        // Act
        var details = await client.GetBooleanDetailsAsync(
            "catalog.show-discontinued-in-search",
            defaultValue: true,   // deliberately the opposite of defaultVariant
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert — the `if` guard in the flag file must short-circuit to defaultVariant "off".
        // Delete the guard and this returns true with reason TARGETING_MATCH.
        details.Value.Should().BeFalse();
        details.Reason.Should().Be("DEFAULT");
    }

    private static ServiceProvider BuildHost(string flagFilePath)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOpenFeature(builder => builder.AddFlagdProvider(o =>
        {
            o.ResolverType = ResolverType.FILE;
            o.OfflineFlagSourcePath = flagFilePath;
            o.OfflinePollIntervalMs = 1_000;
            o.DeadlineMs = 10_000;   // fail fast instead of the 5-minute default
        }));
        return services.BuildServiceProvider();
    }

    private string WriteFlags(int onWeight, int offWeight)
    {
        var path = Path.Combine(_dir, "flags.json");
        File.WriteAllText(path, $$"""
            {
              "$schema": "https://flagd.dev/schema/v0/flags.json",
              "flags": {
                "catalog.show-discontinued-in-search": {
                  "state": "ENABLED",
                  "variants": { "on": true, "off": false },
                  "defaultVariant": "off",
                  "targeting": {
                    "if": [
                      { "var": "targetingKey" },
                      { "fractional": [
                          { "cat": [{ "var": "$flagd.flagKey" }, { "var": "targetingKey" }] },
                          ["on", {{onWeight}}],
                          ["off", {{offWeight}}]
                      ]},
                      null
                    ]
                  }
                }
              }
            }
            """);
        return path;
    }
}
```

Integration test — override the flag file per fixture without touching production wiring. `AddFlagdProvider(Action<…>)` calls `Services.Configure(FlagdProviderOptions.DefaultName, …)`, and .NET Options run configuration delegates in registration order, so a later `Configure` in `ConfigureTestServices` wins:

```csharp
public static IWebHostBuilder UseFlagFile(this IWebHostBuilder webBuilder, string flagFilePath) =>
    webBuilder.ConfigureTestServices(services =>
        services.Configure<FlagdProviderOptions>(FlagdProviderOptions.DefaultName, o =>
        {
            o.ResolverType = ResolverType.FILE;
            o.OfflineFlagSourcePath = flagFilePath;
            o.OfflinePollIntervalMs = 1_000;
            o.DeadlineMs = 10_000;
        }));
```

Testcontainers, when the test's subject is the RPC path itself (worth exactly one test in the suite — the wire, not the rules):

```csharp
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

public sealed class FlagdRpcContainer : IAsyncLifetime
{
    private readonly IContainer _container = new ContainerBuilder()
        .WithImage("ghcr.io/open-feature/flagd:v0.16.1")
        .WithResourceMapping(new FileInfo("TestFlags/flags.json"), "/etc/flagd/")
        .WithCommand("start", "--uri", "file:/etc/flagd/flags.json")
        .WithPortBinding(8013, assignRandomHostPort: true)
        .WithPortBinding(8014, assignRandomHostPort: true)
        // /readyz (not /healthz) is the one that waits for the flag file to parse:
        // it serves HTTP 412 until every sync provider has completed one successful sync.
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilHttpRequestIsSucceeded(r => r.ForPort(8014).ForPath("/readyz")))
        .Build();

    public string Host => _container.Hostname;
    public int Port => _container.GetMappedPublicPort(8013);

    public ValueTask InitializeAsync() => new(_container.StartAsync());
    public ValueTask DisposeAsync() => new(_container.DisposeAsync().AsTask());
}
```

Note the deliberate divergence from the provider repo's own helper, which waits on `/healthz`: liveness is true "as soon as Flagd service is up and running", *before* any sync has completed ([monitoring](https://flagd.dev/reference/monitoring/)). Waiting on `/healthz` races the first flag read.

---

## 9. Observability

### 9.1 flagd's own telemetry

flagd exports OTLP when started with `--metrics-exporter otel` **and** `--otel-collector-uri <host:port>`; without the URI "the collector setup will be ignored and traces will not be exported". Default without those flags is a Prometheus scrape endpoint at `/metrics` on the management port. `OTEL_RESOURCE_ATTRIBUTES` is honoured. Point it at this repo's collector with `--otel-collector-uri otel-collector:4317` (§ 6).

What it emits ([monitoring](https://flagd.dev/reference/monitoring/)):

- **Flag-evaluation metrics** — `feature_flag.flagd.impression`, `feature_flag.flagd.result.reason`, attributed with `feature_flag.key`, `feature_flag.result.variant`, `feature_flag.provider.name` (always `flagd`), `feature_flag.reason`.
- **HTTP server metrics** on the evaluation and OFREP endpoints — `http.server.request.duration` and the two body-size histograms, per the OTel HTTP conventions.
- **gRPC sync metrics** — the standard `rpc.server.*` set, plus `feature_flag.flagd.sync.active_streams` and `feature_flag.flagd.sync.stream.duration` (attributed `selector`, `provider_id`, `reason` ∈ `normal_close` / `deadline_exceeded` / `client_disconnect` / `error`). Under in-process these are the metrics that tell you whether nine providers are actually connected.
- **Spans** — `flagEvaluationService(resolveX)` (server) wrapping `jsonEvaluator(resolveX)` (internal), plus `jsonEvaluator(setState)` on each reload.

### 9.2 The .NET side

Three hooks ship in the core `OpenFeature` package under `OpenFeature.Hooks`: `TraceEnricherHook`, `MetricsHook`, `LoggingHook`. This repo currently registers only `TraceEnricherHook`.

`TraceEnricherHook` adds an `ActivityEvent` named **`feature_flag.evaluation`** to `Activity.Current` on every evaluation ([source](https://github.com/open-feature/dotnet-sdk/blob/main/src/OpenFeature/Hooks/TraceEnricherHook.cs)) — an *event*, not span tags; a dashboard querying span attributes will find nothing. Its attributes come from [`EvaluationEventBuilder`](https://github.com/open-feature/dotnet-sdk/blob/main/src/OpenFeature/Telemetry/EvaluationEventBuilder.cs): `feature_flag.key`, `feature_flag.provider.name`, `feature_flag.result.reason` (lower-cased), `feature_flag.result.variant`, `feature_flag.result.value`, plus `error.type` / `error.message` on failure — and, **read straight out of flag metadata**, `feature_flag.context.id`, `feature_flag.set.id`, `feature_flag.version` from the metadata keys `contextId`, `flagSetId`, `version` ([`TelemetryFlagMetadata`](https://github.com/open-feature/dotnet-sdk/blob/main/src/OpenFeature/Telemetry/TelemetryFlagMetadata.cs)). That is why § 4.4 puts `flagSetId` and `version` in the flag file: those two attributes are free once the file carries them, and they are the only way to see that two services evaluated different rulesets. Anything beyond those three metadata keys needs an explicit `TraceEnricherHookOptions.CreateBuilder().WithFlagEvaluationMetadata(key, m => …)` callback.

**All nine feature-flag attribute names are at OTel stability level "Release Candidate"**, not Stable, as of the [semantic-conventions registry](https://github.com/open-telemetry/semantic-conventions/blob/main/docs/registry/attributes/feature-flag.md) on 2026-08-09: `feature_flag.key`, `feature_flag.provider.name`, `feature_flag.result.reason`, `feature_flag.result.value`, `feature_flag.result.variant`, `feature_flag.context.id`, `feature_flag.error.message`, `feature_flag.set.id`, `feature_flag.version`. The well-known `result.reason` values (`static`, `targeting_match`, `split`, `cached`, `stale`, `disabled`, `default`, `error`, `unknown`) are RC too. Build dashboards on them, but expect a rename window.

Note one attribute-name mismatch between the two sides: flagd's Go metrics use `feature_flag.reason` while the .NET SDK and the semconv registry use `feature_flag.result.reason`. flagd's own doc hedges this — its attributes are "inspired by" the conventions rather than conformant. Any dashboard joining daemon metrics to SDK spans needs a collector-side rename.

---

## 10. Operational limits and gotchas

### 10.1 The `fractional` rebucketing that is already scheduled

flagd carries an **accepted** architecture decision, [*Harden Hashing Consistency And Add Support For Non-string Attributes in Fractional Evaluation*](https://github.com/open-feature/flagd/blob/main/docs/architecture-decisions/fractional-non-string-rand-units.md) (status `accepted`, created 2025-08-21, updated 2025-12-03, timeline "Prior to flagd 1.0 launch"). It replaces raw-UTF-8 hash input with deterministic **CBOR** encoding (RFC 8949 § 4.2.1), and states plainly: "**after this change, all hashes will change, which will result in rebucketing**" and "This change will be backward-compatible in terms of flags schema but will be a **breaking behavioral change for 100% of the users**".

It also changes the *implicit* key: today an omitted bucketing expression concatenates `targetingKey`+`flagKey`; afterwards it becomes a CBOR-encoded two-element array of `flagKey` and `targetingKey`, and the ADR flags this as "different than string concatenation used today". The .NET provider has **not** implemented it — [dotnet-sdk-contrib #516](https://github.com/open-feature/dotnet-sdk-contrib/issues/516) is open since 2025-12-03.

**Consequences for this repo, in order of bite:** (1) any test that pins a specific bucketing key will break on adoption (§ 8.3); (2) if some services run RPC and others in-process/file, they can bucket differently during the window where one implementation has adopted the ADR and the other has not — a second, independent argument for the all-RPC recommendation in § 2.1; (3) whoever is in the 10% cohort today will not be the 10% afterwards, which is fine for a reference solution and would need a comms plan in production.

### 10.2 flagd is pre-1.0

Latest is `flagd/v0.16.1`; [issue #1609 "flagd 1.0 Release"](https://github.com/open-feature/flagd/issues/1609) is open with four remaining `v1.0-prereq` items, two of which are breaking (`feat!: implement numeric coercion contract`). The .NET provider is `0.7.2`. Both are 0.x and both take breaking changes on minor bumps — the provider's own history shows it: `feat!: DISABLED is a successful evaluation (still defaults)` landed in 0.7.0.

### 10.3 Size limits

Three distinct ceilings, none of them a "number of flags" limit:

- `--max-request-body` **1,000,000 bytes** default — requests over it are rejected with HTTP 413 (OFREP) or 429 (Connect). This bounds the *evaluation context* you can send, not the flag set. Relevant if a bulk OFREP evaluation carries a large context.
- `--max-request-header` **1,000,000 bytes**, HTTP 431 over it.
- gRPC sync `maxMsgSize` — **4 MB by default** (Go gRPC's `MaxCallRecvMsgSize`), configurable per source. **This is the real flag-set ceiling for in-process mode**: the entire merged flag document travels in one message. Three flags is not close; a few thousand could be.

### 10.4 Auth and TLS on the sync stream — read this before deploying

**flagd's own evaluation, sync, OFREP and management endpoints have no authentication or authorization of any kind.** The complete `flagd start` flag list has `--server-cert-path` / `--server-key-path` (server-side TLS) and `--cors-origin`, and nothing else security-related — no token, no mTLS client verification, no API key. **Transport encryption yes; access control no.** Anything that can reach port 8013 can read every flag value, and anything that can reach 8015 can download the entire ruleset including targeting rules and any user identifiers embedded in them.

Authentication exists only on the *inbound* side — where flagd fetches flags **from** something: `authHeader` (any scheme), `headers`, and an `oauth` block with `clientID`/`clientSecret`/`tokenURL` or a filesystem `folder` with `ReloadDelayS` rotation, for HTTP sources; `tls` + `certPath` for gRPC sources. **Network policy is the access control**, which in compose means "do not publish 8013–8016 outside the host". Providers do accept a fatal-status-code option so an `UNAUTHENTICATED`/`PERMISSION_DENIED` from an intervening proxy transitions to `PROVIDER_FATAL` rather than retrying forever — an authenticating proxy in front of flagd is therefore a supported pattern.

### 10.5 In-process caveats specific to .NET

- **[dotnet-sdk-contrib #637](https://github.com/open-feature/dotnet-sdk-contrib/issues/637)** (open, 2026-04-29): "FlagD provider (0.6.0) reports Provider Ready when IN_PROCESS is setup and FlagD server is not running" — a regression from 0.4.0. `PROVIDER_READY` fires with an empty ruleset, so every flag returns `FLAG_NOT_FOUND` and falls to code defaults *while the health signal says fine*. If in-process is chosen over the § 2.1 recommendation, do not treat provider readiness as a readiness gate.
- **[dotnet-sdk-contrib #344](https://github.com/open-feature/dotnet-sdk-contrib/issues/344)** (open, 2025-04-16): the provider discards the `EvaluationContext` passed to `InitializeAsync`, where the Java provider merges it into later evaluations. Global/static context set at initialization will not apply.
- **Sync-metadata context enrichment does not exist in FILE mode** — the provider spec states it outright. Static context injected by the daemon via `-X` reaches RPC and in-process consumers but not file-resolver ones, so a rule depending on it silently changes behaviour between test (file) and compose (rpc). Keep targeting rules dependent only on context the application supplies.
- **Schema validation is warn-only.** The in-process/file resolvers validate the flag document against the embedded `flags.json`/`targeting.json` schemas, but "If validation fails a warning log will be generated" — and only if a logger is configured. A typo'd operator degrades to a non-matching rule with a log line, not a startup failure. Configure the logger.

### 10.6 One daemon for many services

The flagd docs address the multi-instance case and the answer is a caution, not a reassurance: you may run "a Kubernetes service in front of a deployment with multiple flagd pods connecting to the same data source. However, if doing so, be aware that **synchronization is not instant**. The service may return different values after a change until all pods have synchronized" ([installation](https://flagd.dev/installation/)). **A single flagd instance has no such window** — which is exactly the shape a nine-process compose stack should use, and a constraint to remember if this ever moves to a replicated deployment. Capacity is not the concern: flagd "can evaluate thousands of flags per second".

### 10.7 Smaller sharp edges

- **`FLAGD_SOCKET_PATH` silently wins.** If set, `FLAGD_HOST`, `FLAGD_PORT`, `FLAGD_TLS` and `FLAGD_SERVER_CERT_PATH` are all ignored.
- **The RPC LRU cache defaults to 10 entries** in .NET (`MaxCacheSize`), against 1000 in the cross-language spec. Fine for three flags; a silent thrash at thirty. And it caches `STATIC` only, so a targeted flag is never cached however large you set it.
- **`FLAGD_DEADLINE_MS` defaults to 300,000 in the .NET FILE resolver** — five minutes of waiting for a missing flag file before initialization gives up. In tests, set it to seconds.
- **`OfflineFlagSourcePath` is resolved with `Path.GetFullPath`**, i.e. against the process working directory. In a `WebApplicationFactory` that is the test host's CWD, not the content root — pass an absolute path.
- **HTTP integer responses come back as strings** from the raw evaluation API (grpc-gateway proto3 JSON mapping). Only affects hand-written `curl`, not the SDK.
- **OFREP is badged EXPERIMENTAL** in flagd's own doc. Fine for a smoke check; not a foundation.

---

## 11. The migration plan for this repo

### 11.1 What is actually broken today

Four defects, all verified by reading the files, three of which the migration deletes rather than fixes:

1. **The percentage rollout never rolls out.** `JsonFlagLoader.FlagFileEntry` declares only `State`, `Variants`, `DefaultVariant` — there is no `Targeting` property, so `System.Text.Json` drops the `targeting` block silently and `Flag<bool>` is built with no `contextEvaluator`. `state` is parsed and then never passed to `Flag<T>`'s `disabled` parameter either, so `"state": "DISABLED"` does nothing.
2. **No call site supplies a targeting key.** All three (`SearchProductsQueryHandler`, `HomePageCacheWarmer`, `CheckoutSagaOrchestrator`) call `GetBooleanValueAsync(key, defaultValue, cancellationToken: ct)` with no `EvaluationContext`.
3. **The flag file is unreachable at runtime, in every shape.** `flags.json` sits at the repo root; no `.csproj` copies it to output, no `Dockerfile` copies it, and `docker-compose.yaml` contains no occurrence of the string `flags.json`. `FeatureFlagsOptions.FilePath` defaults to a *relative* `"flags.json"` resolved against the service content root. So `JsonFlagLoader` takes its `File.Exists` → empty-dictionary branch in all nine processes, and every flag resolves to the `defaultValue` at the call site.
4. **Catalog points at a filename that exists nowhere.** `services/Catalog/Catalog.Api/appsettings.json` sets `"FeatureFlags": { "FilePath": "feature-flags.json" }`; BFF and saga set `"flags.json"`. A repo-wide search for `feature-flags.json` matches only that one line.

Defects 1, 3 and 4 all disappear when flagd owns loading and evaluation. Defect 2 must be fixed by hand, and must be fixed *first* (§ 4.4).

### 11.2 Package changes

| File | Change |
|---|---|
| `platform/Directory.Packages.props` | `OpenFeature` and `OpenFeature.Hosting` **2.13.0 → 2.14.0** (lines 41–42); **add** `<PackageVersion Include="OpenFeature.Providers.Flagd" Version="0.7.2"/>` |
| `services/Directory.Packages.props` | `OpenFeature` **2.13.0 → 2.14.0** (line 81) |
| `saga/Directory.Packages.props` | `OpenFeature` **2.13.0 → 2.14.0** (line 60) |
| `test/Directory.Packages.props` | Nothing, unless the Testcontainers RPC test of § 8.5 is written — it needs no new package (`Testcontainers` core is already present via the existing container fixtures) |
| `src/Directory.Packages.props` | Nothing — the BFF consumes flags through `Platform.ServiceDefaults` |

`OpenFeature.Providers.Flagd` belongs at the `platform/` level only: it is an infrastructure concern of `Platform.ServiceDefaults`, and the `services/` and `saga/` levels need just the `OpenFeature` abstractions their handlers already import. **Never put `Version=` on a `PackageReference`.** After editing, regenerate lock files with a single `dotnet restore` **without** `--locked-mode` and commit the lock delta — `packages.lock.json` is agent-deny-protected and must never be hand-edited.

### 11.3 What happens to each file

**`JsonFlagLoader.cs` — delete it.** The argument for keeping it as the offline/test path fails on three counts, and it is worth spelling out because "keep it for tests" is the reflex:

- The FILE resolver *is* the offline path, and it is strictly better: it parses the real schema, evaluates real JsonLogic including `fractional`, validates against the published schema, and watches the file for changes. `JsonFlagLoader` does none of that — it cannot express a single targeting rule.
- Keeping both means two parsers for one file format. The test path would then prove things about a loader production does not use, which is the failure mode the current tests already have.
- Its only genuinely distinct behaviour is "a missing or malformed file yields an empty flag set and a warning instead of throwing". flagd's FILE resolver instead waits up to `DeadlineMs` for the file and raises `PROVIDER_ERROR` on a parse failure. **That is the better behaviour** — a malformed flag file should be loud. And it is not a runtime hazard, because `IFeatureClient` still returns each call site's `defaultValue` regardless of provider state.

Delete `JsonFlagLoader.cs` and `JsonFlagLoaderTests.cs` together in the slice that introduces the FILE resolver.

**`FeatureFlagsOptions.cs` — reshape, don't delete.** It becomes the repo's own binding surface over the two things that vary per environment:

```csharp
public sealed class FeatureFlagsOptions
{
    public const string Section = "FeatureFlags";

    /// <summary>rpc against the flagd daemon, or file against a local flag definition.</summary>
    public ResolverType Resolver { get; set; } = ResolverType.FILE;

    /// <summary>flagd daemon host. Used when <see cref="Resolver"/> is RPC.</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>flagd evaluation port. Used when <see cref="Resolver"/> is RPC.</summary>
    public int Port { get; set; } = 8013;

    /// <summary>Absolute path to the flag definition. Used when <see cref="Resolver"/> is FILE.</summary>
    public string FilePath { get; set; } = "flags.json";
}
```

Defaulting `Resolver` to `FILE` keeps "clone and run the tests" working with no environment at all; compose sets `FeatureFlags__Resolver=Rpc`. The alternative — reading `FLAGD_*` directly and dropping the options class — is fewer moving parts but gives up `ValidateOnStart`, which is what would have caught defect 4.

**`FeatureFlagsServiceCollectionExtensions.cs` — same shape, different provider.**

```csharp
public static IServiceCollection AddFeatureFlags(
    this IServiceCollection services,
    IConfiguration configuration)
{
    ArgumentNullException.ThrowIfNull(services);
    ArgumentNullException.ThrowIfNull(configuration);

    services.AddOptionsWithValidateOnStart<FeatureFlagsOptions>()
        .BindConfiguration(FeatureFlagsOptions.Section)
        .Validate(o => o.Resolver != ResolverType.FILE || File.Exists(o.FilePath),
            "FeatureFlags:FilePath does not exist; the FILE resolver cannot start.");

    services.AddOpenFeature(builder =>
    {
        builder.AddFlagdProvider(o =>
        {
            var opts = configuration.GetSection(FeatureFlagsOptions.Section)
                .Get<FeatureFlagsOptions>() ?? new FeatureFlagsOptions();
            o.ResolverType = opts.Resolver;
            o.Host = opts.Host;
            o.Port = opts.Port;
            o.OfflineFlagSourcePath = opts.FilePath;
            o.CacheEnabled = opts.Resolver == ResolverType.RPC;
            o.MaxCacheSize = 100;
        });

        builder.AddHook(new TraceEnricherHook());
        builder.AddHook(new MetricsHook());
    });

    return services;
}
```

The `Validate` call is the fix for defect 4 — `feature-flags.json` would have failed startup instead of silently disabling every flag. Note that `AddFlagdProvider`'s options delegate cannot take `IServiceProvider`, so the section is read from `IConfiguration` directly rather than from `IOptions<T>`; the options object stays for validation and for documenting the surface.

**`flags.json` — move to `src/flagd/flags.json` and rewrite per § 4.4.** Reasons in § 6.

**The existing tests.** `JsonFlagLoaderTests.cs` — delete with the loader. `FeatureFlagsServiceCollectionExtensionsTests.cs` — **rewrite, and note that one of its two tests breaks on the 2.14.0 bump before flagd is even involved**: `AddFeatureFlags_WithLoadedFlagsJson_EvaluatesFlag` resolves the client via `Api.Instance.GetClient()`, and 2.14.0 no longer registers the global instance in DI. It must resolve `IFeatureClient` from the container. `AddFeatureFlags_RegistersFeatureApi` asserts `Api.Instance.Should().NotBeNull()`, which is true of a static property whether or not `AddFeatureFlags` was ever called — delete it outright rather than port it. Replace both with the § 8.5 shape: a real FILE-resolver evaluation asserting value **and** reason.

**Downstream test fixtures.** `test/Catalog.IntegrationTests/Common/IntegrationTestFixture.cs`, `test/EShop.BFF.IntegrationTests/Common/BffTestHostExtensions.cs` and `saga/SagaOrchestrators.UnitTests/Checkout/CheckoutFeatureClientStub.cs` all substitute `IFeatureClient`. Their stated justification — process-global `Api.Instance` contamination — evaporates at 2.14.0 (§ 8.4). Replace the two integration fixtures with per-fixture flag files (`UseFlagFile`, § 8.5). **Keep `CheckoutFeatureClientStub.WithPaymentThenStockAwaiting`**: its purpose is not to pin a value but to force a genuinely asynchronous completion so a `Then(async …)` regression is caught, and its XML doc says so. That is a legitimate test double for a sequencing test and no provider replaces it.

### 11.4 Where flagd sits, and how one checkout stays consistent

One flagd container, in profiles `core` and `full`. All nine processes evaluate against it over RPC. **Do not** evaluate in the BFF and pass results downstream — the saga and the Kafka consumers evaluate outside any HTTP request and would have no source, a point [`feature-flag-providers.md` § 5](feature-flag-providers.md#5-what-this-repo-would-actually-do) already makes and this note endorses.

Consistency for a single checkout needs three things, only one of which is flagd's:

1. **One evaluator.** RPC gives it (§ 2.1). This is what removes the multi-second divergence window that in-process or file would introduce across nine independent watchers.
2. **The same targeting key at every hop.** flagd's bucketing is deterministic over the key you supply and nothing else; two services hashing different keys get different cohorts however consistent the daemon is. `buyerId` for user-facing flags and `orderId` for workflow flags (as ADR-0014 already specifies) must be attached at each call site and carried across the Kafka hop alongside the correlation id.
3. **Evaluate once for anything order-changing.** `checkout.payment-then-stock` decides step *order*; re-evaluating it at each transition can land a saga in a topology neither branch expects. **This repo already does the right thing** — the orchestrator reads it once on `OrderCreatedSagaEvent` and stashes it on `[NotMapped] CheckoutSagaState.PaymentThenStockEnabled`. Nothing to change; worth a comment pointing at this section so nobody "fixes" it later.

`feature_flag.set.id` and `feature_flag.version` (§ 9.2) make ruleset disagreement *detectable*. Bump `metadata.version` on every edit to `src/flagd/flags.json` and a trace that spans two rulesets is visible in Jaeger rather than mysterious.

### 11.5 Vertical slices, in dependency order

Each is independently shippable, independently testable, and leaves the tree green.

**Slice 0 — OpenFeature 2.13.0 → 2.14.0.** No flag behaviour changes. Bump the three `Directory.Packages.props` files; regenerate lock files. Breaks `AddFeatureFlags_WithLoadedFlagsJson_EvaluatesFlag` (global `Api.Instance` no longer registered) — fix it to resolve `IFeatureClient` from the container. Ships the isolated-API-per-host property that later slices depend on. *Independently valuable even if flagd is abandoned.*

**Slice 1 — attach an evaluation context at all three call sites.** Still on `InMemoryProvider`. `SearchProductsQueryHandler` gets `TargetingKey = buyerId` (which means threading the buyer onto `SearchProductsQuery` — check whether the public search endpoint has one; if it does not, that is a design question to settle here, not later); `CheckoutSagaOrchestrator` gets `TargetingKey = orderId`; `HomePageCacheWarmer` gets none, deliberately — it is a kill switch, not a rollout, and a startup `BackgroundService` has no subject. Test with `InMemoryProvider` + a `contextEvaluator` that returns a variant keyed on the targeting key, so the assertion is "the context reached the provider" rather than "a mock was called". **Hard prerequisite for slice 2**: `fractional` without a targeting key buckets an entire service as one subject.

**Slice 2 — flagd FILE resolver replaces `JsonFlagLoader`, Catalog first.** Add the package to `platform/Directory.Packages.props`; reshape `FeatureFlagsOptions` and `AddFeatureFlags`; create `src/flagd/flags.json` per § 4.4 and delete the root `flags.json`; fix Catalog's `feature-flags.json` path; delete `JsonFlagLoader.cs` + `JsonFlagLoaderTests.cs`. Point every service's `FeatureFlags:FilePath` at an absolute path. **This is the tracer bullet** — it is the first moment `catalog.show-discontinued-in-search` can return `on`, and the test that proves it (§ 8.5) is the first test in the repo that exercises a real targeting rule. No container.

**Slice 3 — flagd in docker-compose, RPC for the containerised topology.** Add the § 6 service block; set `FeatureFlags__Resolver=Rpc` + host/port on `catalog.api`; append the § 6 smoke check to the gate. FILE remains the default for tests and `dotnet run`. Now both resolver paths are live and the difference is one environment variable.

**Slice 4 — roll the seam to BFF and the saga.** These are the only other two processes calling `AddFeatureFlags` today (`src/EShop.BFF/EShop.BFF.Infrastructure/Common/InfrastructureDependencyInjection.cs`, `saga/SagaOrchestrators/Program.cs`). Add their compose env + `depends_on`. Replace `BffTestHostExtensions.UseWarmFlag` with `UseFlagFile`. Keep `CheckoutFeatureClientStub.WithPaymentThenStockAwaiting` (§ 11.3).

**Slice 5 — retire the remaining `IFeatureClient` substitutes.** `Catalog.IntegrationTests/Common/IntegrationTestFixture.cs` and the assertion in `SearchProductsTests` that checks `FeatureClient.Received()`. Replace with a per-fixture flag file and an assertion on the observable outcome — the discontinued product appearing in the response body — which is already there and is the only part of that test that survives a mutation.

**Slice 6 — observability.** Add `MetricsHook` alongside `TraceEnricherHook`; add `--metrics-exporter otel --otel-collector-uri otel-collector:4317` to the compose block if slice 3 deferred it; add a Grafana panel over `feature_flag.flagd.impression` and the SDK's `feature_flag.evaluation` events. Verify `feature_flag.set.id` and `feature_flag.version` appear in Jaeger.

**Slice 7 — amend ADR-0014.** § 11.6. Do it last, when the shipped shape is known, rather than writing it twice.

Slices 0+1 could merge if the targeting-key question is trivial. Slices 3+4 could merge if the compose edit is mechanical. Nothing else should.

### 11.6 ADR amendments

ADR-0014 is *Accepted* and this repo permits rewriting ADRs inline. Six changes:

1. **Correct "CNCF-graduated" → CNCF Incubating.**
2. **Delete the `.AddFileProvider(...)` and `.AddLaunchDarklyProvider(...)` code samples** — neither is a real API. Replace with the § 11.3 registration.
3. **Replace the "JSON file format (simplified)" block** — it advertises a schema that is neither OpenFeature's nor flagd's, and it is the source of the invented `{"op":"percentage"}` rule now sitting in `flags.json`. Replace with § 4.4 and link the [flagd flag-definition reference](https://flagd.dev/reference/flag-definitions/).
4. **Replace "OpenFeature + local JSON-file provider" with "OpenFeature + flagd"** in the Decision, and rewrite the Consequences: the "swap the provider via DI in production" claim is weaker than it reads — portability runs through OFREP, not through the provider ecosystem, per [`feature-flag-providers.md` § 2](feature-flag-providers.md#2-the-openfeature-question-comes-first-because-it-reframes-everything-else).
5. **Fix the Negative consequence about hot reload** — "a 30-second fallback poll is used" describes nothing that exists. flagd polls `os.Stat` at 1s outside Kubernetes; the .NET FILE resolver polls at 5s (§ 7).
6. **Add an explicit UI statement to Consequences → Negative:** flagd has no UI, no audit log, no approvals and no RBAC; the flag file in Git plus the repo's review gates *are* the governance model. This is the single fact most likely to surprise a reader later, and § 1 exists because it is not written down anywhere in the repo today.

Also worth adding: a Risks entry for § 10.1's scheduled rebucketing, since it will change who is in the 10% cohort at some future provider bump and there is no way to opt out.

---

## 12. Negative findings

**Where this note's own brief was wrong.** The schemas live in **`open-feature/flagd-schemas`**, not `open-feature/schemas` — no repository by the latter name exists in the `open-feature` organisation (verified against the full org repo listing).

**Absence claims, and the surfaces that back them.**

- *No Testcontainers module for flagd exists in any language.* Searched: the `src/` directory of `testcontainers/testcontainers-dotnet` (66 modules), `modules/` of `testcontainers/testcontainers-java` (63) and `testcontainers/testcontainers-go` (86), all via the GitHub contents API; plus the nuget.org search API for `flagd` (7 total hits, enumerated in § 8.1) and for `Testcontainers flagd` (0 hits). Three module registries and the package index — the two places a .NET consumer would find one.
- *No first-party flagd UI exists.* Searched: the complete `open-feature` org repo listing (60 repos), the flagd repo's own top-level tree (`ui/` absent; `playground-app/` is a Vite/React *flag-behaviour sandbox* embedded in the docs, not a management surface — its README says it "allows users to define flags and experiment with various inputs"), the flagd docs tree, and the GitHub repository search index for `flagd ui`. flagd's own introduction states the absence directly.
- *No hosted vendor speaks `flagd.sync.v1`.* Surfaces in § 1.4. This is a **not-found**, not a proof: it rests on flagd's own sync-source enumeration and the buf schema registry, neither of which would necessarily list a third-party implementer.

**Docs that contradict the source, reported rather than smoothed over.**

- **The `flagd` .NET provider README shows `.WithPort("8015")` — a string. `FlagdConfigBuilder` has only `WithPort(int)`.** The README sample does not compile. (Read from `FlagdConfig.cs`.)
- **The same README's DI sample calls `config.AddHostedFeatureLifecycle()`, which the .NET SDK deprecated in 2.9.0** (`feat: Deprecate AddHostedFeatureLifecycle method`, [PR #531](https://github.com/open-feature/dotnet-sdk/pull/531)). `AddOpenFeature` registers `HostedFeatureLifecycleService` unconditionally.
- **flagd's troubleshooting page curls `http://localhost:8014/ofrep/v1/evaluate/flags`.** OFREP is on **8016** — both the `--ofrep-port` CLI default and the dedicated [OFREP reference](https://flagd.dev/reference/flagd-ofrep/) say so; 8014 is the management port. The troubleshooting page is wrong.
- **Three .NET defaults contradict the cross-language provider specification.** Spec vs `FlagdProviderOptions`: cache `lru` vs **disabled**; `maxCacheSize` 1000 vs **10**; `deadlineMs` 500 vs **300000**. The .NET provider also implements `FLAGD_MAX_EVENT_STREAM_RETRIES`, which the spec does not list, and omits five options the spec does (§ 5.1). Configure explicitly rather than relying on either document.
- **The flag-definition reference lists `$schema` as required; the JSON schema's `providerConfig` requires only `flags`.** Include `$schema` anyway — it is what gives editors validation — but a document without it is still valid to flagd.
- **The `--sources` OAuth example passes a `timeoutS` field that the source-configuration option table does not document.** Used but unlisted; treat as undocumented.

**Verified-by-source rather than by docs.** The Windows/bind-mount answer in § 7 is not stated anywhere in flagd's prose — the sync-configuration page says only that `file` "defaults to using `fsnotify` when flagd detects it is running in kubernetes and `fileinfo` in all other cases". The mechanism (`os.Getenv("KUBERNETES_SERVICE_HOST")`) and the interval (`1 * time.Second`) were read out of `syncbuilder.go` and `fileinfo_watcher.go`. Likewise the "no healthcheck possible" conclusion in § 6 comes from the image config blob on ghcr.io (`User: 65532:65532`, `Entrypoint: ["/flagd-build"]`, bazel-built distroless layers), not from a Dockerfile — the flagd repo's root `Dockerfile` builds the *documentation site* (`FROM squidfunk/mkdocs-material`), which is a trap for anyone checking the obvious file.

**Labelled inference — extrapolation to this repo, not vendor guidance.**

- The resolver-mode recommendation (§ 2.1), the profile choice, port allocation, memory limits and the `src/flagd/` relocation (§ 6), the delete-vs-keep argument for `JsonFlagLoader` (§ 11.3), and the slice sequencing (§ 11.5) are all my judgement applied to this repo's constraints. No source recommends any of them.
- **That RPC and .NET in-process bucket identically today for this repo's inputs is inference.** The daemon hashes UTF-8 bytes with `twmb/murmur3`; the .NET provider hashes with the `murmurhash` package. For ASCII bucketing values the byte sequences are identical, so the hashes should agree — but flagd's own ADR exists precisely because implementations have diverged on non-ASCII input, and nothing I read states cross-implementation equivalence as a current guarantee. Do not mix resolver modes across services and assume one cohort.
- **That a later `services.Configure<FlagdProviderOptions>(DefaultName, …)` overrides `AddFlagdProvider`'s** (§ 8.5) follows from standard .NET Options ordering plus the observed `Services.Configure(FlagdProviderOptions.DefaultName, options)` call in `FeatureBuilderExtensions.cs`. It is not documented by the provider; verify it with the first test that uses it.
- **Compose memory sizing** (128M / `GOMEMLIMIT=100MiB`) is scaled up from the OpenTelemetry demo's flagd entry (75M / `GOMEMLIMIT=60MiB`), which is the only first-party sizing figure I found.

**Discarded.** `open-feature/flagd`'s root `Dockerfile` (docs site, not the daemon). A GitHub code-search for "UI" across the flagd docs returned HTTP 401 — code search requires authentication — so the UI-absence claim rests on the org repo listing, the repo trees, and flagd's explicit statement rather than on full-text search. Two GitHub REST calls hit the anonymous rate limit mid-session and were re-run through the authenticated `gh` CLI; the values in this note are from the authenticated runs.

## Related

- [`feature-flag-providers.md`](feature-flag-providers.md) — owns the provider comparison and the flagd recommendation this note implements.
- [ADR-0014](../adr/0014-feature-flags-openfeature.md) — the accepted decision; six amendments proposed in § 11.6.
- [ADR-0004](../adr/0004-checkout-saga-topology.md) — the topology `checkout.payment-then-stock` gates.
- [ADR-0016](../adr/0016-redis-split-basket-cache.md) — the compose precedent for a storage-shaped dependency in both `core` and `full`.
